using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

internal sealed partial class OptionsTypeMetadata
{
    private static bool NoDeclaredConstructorCanOverwriteProperty(INamedTypeSymbol rootType, IPropertySymbol property)
    {
        return TryResolveRuntimeConstructorEffect(rootType, property, compilation: null, out var assignedKind) &&
               assignedKind is null;
    }

    private static bool TryResolveRuntimeConstructorEffect(
        INamedTypeSymbol rootType,
        IPropertySymbol property,
        Compilation? compilation,
        out RecursiveDefaultKind? assignedKind)
    {
        assignedKind = null;

        // Only the constructor chain the binder actually executes matters: the runtime-selected
        // constructor on the root type, then each implicitly chained accessible parameterless
        // base constructor. Unused public overloads and private factory constructors never run.
        var constructor = SelectRuntimeBindingConstructor(rootType);
        while (true)
        {
            if (constructor is null)
            {
                return false;
            }

            ConstructorDeclarationSyntax? declaration = null;
            var hasNonConstructorDeclaration = false;
            foreach (var reference in constructor.DeclaringSyntaxReferences)
            {
                if (reference.GetSyntax() is ConstructorDeclarationSyntax constructorSyntax)
                {
                    declaration = constructorSyntax;
                }
                else
                {
                    hasNonConstructorDeclaration = true;
                }
            }

            if (declaration is null)
            {
                // Implicit default constructors and class/record primary constructors cannot
                // write an existing property, but a primary constructor with explicit base
                // arguments selects a base overload this walk cannot resolve, and a syntaxless
                // constructor from a referenced assembly cannot be proven harmless.
                if (hasNonConstructorDeclaration)
                {
                    if (PrimaryConstructorHasExplicitBaseArguments(constructor))
                    {
                        return false;
                    }
                }
                else if (!constructor.IsImplicitlyDeclared)
                {
                    return false;
                }
            }
            else
            {
                // Explicit chains with arguments can target overloads this walk cannot resolve;
                // a zero-argument `: base()` resolves to the same constructor the implicit chain
                // selects, and a zero-argument `: this()` is followed below.
                if (declaration.Initializer is { } chainInitializer &&
                    chainInitializer.ArgumentList.Arguments.Count > 0)
                {
                    return false;
                }

                if (!TryClassifyConstructorPropertyWrite(declaration, constructor, property, compilation, out var writeKind))
                {
                    return false;
                }

                if (writeKind is not null)
                {
                    // Base constructors run before this write, so the chain above is irrelevant,
                    // and every more-derived constructor was already proven non-writing.
                    assignedKind = writeKind;
                    return true;
                }

                if (declaration.Initializer?.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisConstructorInitializer) == true)
                {
                    constructor = SelectParameterlessConstructor(constructor.ContainingType);
                    continue;
                }
            }

            var baseType = constructor.ContainingType.BaseType;
            if (baseType is null || baseType.SpecialType == SpecialType.System_Object)
            {
                return true;
            }

            constructor = SelectImplicitlyChainedConstructor(baseType);
        }
    }

    private static IMethodSymbol? SelectParameterlessConstructor(INamedTypeSymbol type)
    {
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0)
            {
                return constructor;
            }
        }

        return null;
    }

    private static IMethodSymbol? SelectRuntimeBindingConstructor(INamedTypeSymbol type)
    {
        IMethodSymbol? parameterless = null;
        IMethodSymbol? singleParameterized = null;
        var parameterizedCount = 0;
        foreach (var constructor in type.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            if (constructor.Parameters.Length == 0)
            {
                parameterless = constructor;
            }
            else
            {
                parameterizedCount++;
                singleParameterized = constructor;
            }
        }

        if (parameterless is not null)
        {
            return parameterless;
        }

        return parameterizedCount == 1 ? singleParameterized : null;
    }

    private static IMethodSymbol? SelectImplicitlyChainedConstructor(INamedTypeSymbol baseType)
    {
        foreach (var constructor in baseType.InstanceConstructors)
        {
            if (constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility != Accessibility.Private)
            {
                return constructor;
            }
        }

        return null;
    }

    private static bool PrimaryConstructorHasExplicitBaseArguments(IMethodSymbol constructor)
    {
        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is TypeDeclarationSyntax typeDeclaration &&
                typeDeclaration.BaseList?.Types.OfType<PrimaryConstructorBaseTypeSyntax>().Any() == true)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryClassifyConstructorPropertyWrite(
        ConstructorDeclarationSyntax declaration,
        IMethodSymbol constructor,
        IPropertySymbol property,
        Compilation? compilation,
        out RecursiveDefaultKind? writeKind)
    {
        writeKind = null;

        // Name hiding or shadowing makes the syntax match unreliable.
        if (PropertyNameIsHidden(constructor.ContainingType, property) ||
            ConstructorShadowsPropertyName(declaration, property))
        {
            return false;
        }

        if (declaration.ExpressionBody is { } expressionBody)
        {
            if (!IsSimpleParameterOrLiteralAssignment(expressionBody.Expression, constructor))
            {
                return false;
            }

            if (expressionBody.Expression is AssignmentExpressionSyntax expressionAssignment &&
                IsPropertyAssignmentTarget(expressionAssignment.Left, property))
            {
                writeKind = ClassifyAssignedDefaultValue(expressionAssignment.Right, property.Type, compilation);
            }

            return true;
        }

        if (declaration.Body is null)
        {
            return false;
        }

        foreach (var statement in declaration.Body.Statements)
        {
            if (statement is not ExpressionStatementSyntax expressionStatement ||
                !IsSimpleParameterOrLiteralAssignment(expressionStatement.Expression, constructor))
            {
                return false;
            }

            if (expressionStatement.Expression is AssignmentExpressionSyntax assignment &&
                IsPropertyAssignmentTarget(assignment.Left, property))
            {
                // Statements are sequential and side-effect-free, so the last write wins.
                writeKind = ClassifyAssignedDefaultValue(assignment.Right, property.Type, compilation);
            }
        }

        return true;
    }

    private static bool HasSatisfyingConstructorParameterDefault(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        bool allowEmptyStrings)
    {
        var bindableConstructors = rootType.InstanceConstructors
            .Where(static constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Length > 0)
            .ToArray();
        if (bindableConstructors.Length != 1)
        {
            return false;
        }

        foreach (var parameter in bindableConstructors[0].Parameters)
        {
            if (IsConstructorParameterForProperty(parameter, property))
            {
                return parameter.HasExplicitDefaultValue &&
                       DefaultValueSatisfiesRequired(parameter.ExplicitDefaultValue, allowEmptyStrings) &&
                       ConstructorParameterDefinitelyReachesProperty(bindableConstructors[0], parameter, property);
            }
        }

        return false;
    }

    private static bool ConstructorParameterDefinitelyReachesProperty(
        IMethodSymbol constructor,
        IParameterSymbol parameter,
        IPropertySymbol property)
    {
        // Positional record parameters initialize their synthesized property directly.
        foreach (var propertyReference in property.DeclaringSyntaxReferences)
        {
            if (propertyReference.GetSyntax() is ParameterSyntax parameterSyntax &&
                parameter.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() == parameterSyntax))
            {
                return true;
            }
        }

        // Name hiding makes the syntax match unreliable: an assignment to a hiding member never
        // reaches the hidden required property.
        if (PropertyNameIsHidden(constructor.ContainingType, property))
        {
            return false;
        }

        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            // Constructor initializers run before the body, so a `: base(...)` or `: this(...)`
            // chain cannot clear a value the body assigns afterwards.
            if (reference.GetSyntax() is not ConstructorDeclarationSyntax declaration ||
                ConstructorShadowsPropertyName(declaration, property))
            {
                continue;
            }

            if (declaration.ExpressionBody?.Expression is { } expressionBody)
            {
                // The same side-effect-free target rule as block bodies applies: a custom setter
                // could mutate the value instead of storing the parameter.
                if (IsSimpleParameterOrLiteralAssignment(expressionBody, constructor) &&
                    IsDirectParameterToPropertyAssignment(expressionBody, parameter, property))
                {
                    return true;
                }

                continue;
            }

            if (declaration.Body is null)
            {
                continue;
            }

            // The proof only holds when the body contains nothing but simple parameter-or-literal
            // assignments: helper calls, compound expressions, or control flow could mutate the
            // property after the parameter assignment runs.
            var assignsParameter = false;
            var bodyIsOnlySimpleAssignments = true;
            foreach (var statement in declaration.Body.Statements)
            {
                if (statement is not ExpressionStatementSyntax expressionStatement ||
                    !IsSimpleParameterOrLiteralAssignment(expressionStatement.Expression, constructor))
                {
                    bodyIsOnlySimpleAssignments = false;
                    break;
                }

                if (IsDirectParameterToPropertyAssignment(expressionStatement.Expression, parameter, property))
                {
                    assignsParameter = true;
                }
            }

            if (!bodyIsOnlySimpleAssignments || !assignsParameter)
            {
                continue;
            }

            // Any other write to the property could overwrite the parameter value, so every
            // property write must be the direct parameter assignment.
            var allPropertyWritesUseParameter = declaration.Body
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(assignment => IsPropertyAssignmentTarget(assignment.Left, property))
                .All(assignment => IsDirectParameterToPropertyAssignment(assignment, parameter, property));

            if (allPropertyWritesUseParameter)
            {
                return true;
            }
        }

        return false;
    }

    private static bool PropertyNameIsHidden(INamedTypeSymbol type, IPropertySymbol property)
    {
        var membersWithName = 0;
        for (var current = type; current is not null; current = current.BaseType)
        {
            membersWithName += current.GetMembers(property.Name).Length;
        }

        return membersWithName > 1;
    }

    private static bool ConstructorShadowsPropertyName(ConstructorDeclarationSyntax declaration, IPropertySymbol property)
    {
        // A parameter or local named exactly like the property would capture the assignment,
        // leaving the property untouched.
        if (declaration.ParameterList.Parameters.Any(parameter =>
                string.Equals(parameter.Identifier.ValueText, property.Name, StringComparison.Ordinal)))
        {
            return true;
        }

        return declaration.Body is not null &&
               declaration.Body
                   .DescendantNodes()
                   .OfType<VariableDeclaratorSyntax>()
                   .Any(declarator => string.Equals(declarator.Identifier.ValueText, property.Name, StringComparison.Ordinal));
    }

    private static bool IsSimpleParameterOrLiteralAssignment(ExpressionSyntax expression, IMethodSymbol constructor)
    {
        if (expression is not AssignmentExpressionSyntax assignment ||
            !assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression))
        {
            return false;
        }

        var left = StripInitializerWrappers(assignment.Left);
        var targetName = left switch
        {
            IdentifierNameSyntax identifierTarget => identifierTarget.Identifier.ValueText,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberAccess =>
                memberAccess.Name.Identifier.ValueText,
            _ => null
        };

        // An unqualified identifier that matches a constructor parameter writes the parameter,
        // not a member, even when a same-named field or property exists.
        if (targetName is null ||
            (left is IdentifierNameSyntax &&
             constructor.Parameters.Any(parameter =>
                 string.Equals(parameter.Name, targetName, StringComparison.Ordinal))))
        {
            return false;
        }

        // A custom setter on the assigned member could mutate other properties, so the target
        // must be a field or an auto-implemented property.
        if (!IsSideEffectFreeAssignmentTarget(constructor.ContainingType, targetName))
        {
            return false;
        }

        var right = StripInitializerWrappers(assignment.Right);
        return right is LiteralExpressionSyntax ||
               IsArgumentFreeCreation(right) ||
               (right is IdentifierNameSyntax identifier &&
                constructor.Parameters.Any(parameter =>
                    string.Equals(parameter.Name, identifier.Identifier.ValueText, StringComparison.Ordinal)));
    }

    private static bool IsArgumentFreeCreation(ExpressionSyntax expression)
    {
        // A creation with no arguments and no initializer cannot reference the enclosing
        // instance, so it cannot mutate other properties.
        return expression switch
        {
            ObjectCreationExpressionSyntax creation =>
                (creation.Initializer is null || creation.Initializer.Expressions.Count == 0) &&
                (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0),
            ImplicitObjectCreationExpressionSyntax implicitCreation =>
                (implicitCreation.Initializer is null || implicitCreation.Initializer.Expressions.Count == 0) &&
                implicitCreation.ArgumentList.Arguments.Count == 0,
            CollectionExpressionSyntax collectionExpression => collectionExpression.Elements.Count == 0,
            _ => false
        };
    }

    private static bool IsSideEffectFreeAssignmentTarget(INamedTypeSymbol type, string memberName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(memberName))
            {
                return member switch
                {
                    IFieldSymbol => true,
                    // A get-only auto-property assigned in a constructor writes the backing field
                    // directly; a settable property needs an auto-implemented setter. Overridable
                    // members can dispatch to a derived accessor with side effects.
                    IPropertySymbol property =>
                        !property.IsVirtual && !property.IsAbstract && !property.IsOverride &&
                        (property.SetMethod is null
                            ? IsAutoImplementedAccessor(property.GetMethod)
                            : IsAutoImplementedAccessor(property.SetMethod)),
                    _ => false
                };
            }
        }

        return false;
    }

    private static bool IsAutoImplementedAccessor(IMethodSymbol? accessorMethod)
    {
        if (accessorMethod is null)
        {
            return false;
        }

        if (accessorMethod.IsImplicitlyDeclared)
        {
            return true;
        }

        foreach (var reference in accessorMethod.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not AccessorDeclarationSyntax accessor ||
                accessor.Body is not null ||
                accessor.ExpressionBody is not null)
            {
                return false;
            }
        }

        return accessorMethod.DeclaringSyntaxReferences.Length > 0;
    }

    private static bool IsDirectParameterToPropertyAssignment(
        ExpressionSyntax expression,
        IParameterSymbol parameter,
        IPropertySymbol property)
    {
        return expression is AssignmentExpressionSyntax assignment &&
               assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) &&
               IsPropertyAssignmentTarget(assignment.Left, property) &&
               StripInitializerWrappers(assignment.Right) is IdentifierNameSyntax identifier &&
               string.Equals(identifier.Identifier.ValueText, parameter.Name, StringComparison.Ordinal);
    }

    private static bool IsPropertyAssignmentTarget(ExpressionSyntax expression, IPropertySymbol property)
    {
        return StripInitializerWrappers(expression) switch
        {
            IdentifierNameSyntax identifier =>
                string.Equals(identifier.Identifier.ValueText, property.Name, StringComparison.Ordinal),
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax } memberAccess =>
                string.Equals(memberAccess.Name.Identifier.ValueText, property.Name, StringComparison.Ordinal),
            _ => false
        };
    }
}
