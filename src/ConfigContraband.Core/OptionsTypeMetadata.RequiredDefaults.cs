using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

internal sealed partial class OptionsTypeMetadata
{
    private static bool HasValidationAttribute(ISymbol symbol)
    {
        return symbol.GetAttributes().Any(attribute => InheritsFrom(attribute.AttributeClass, "System.ComponentModel.DataAnnotations.ValidationAttribute"));
    }

    private static bool IsRequired(ISymbol symbol)
    {
        if (symbol.GetAttributes().Any(attribute => IsRequiredAttribute(attribute.AttributeClass)))
        {
            return symbol is not IPropertySymbol property ||
                   !property.Type.IsValueType ||
                   IsNullableValueType(property.Type);
        }

        return false;
    }

    private static bool IsRequiredAttribute(INamedTypeSymbol? attributeClass)
    {
        if (attributeClass is null)
        {
            return false;
        }

        // The runtime validator enforces RequiredAttribute and any subclass that inherits its
        // check, so match by inheritance rather than an exact type name. A subclass that overrides
        // IsValid may weaken the check (e.g. accept a missing value), so it can no longer be proven
        // required — stay conservative and treat only RequiredAttribute itself or a subclass that
        // does not override IsValid as required.
        var overridesIsValid = false;
        for (var current = attributeClass; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), "System.ComponentModel.DataAnnotations.RequiredAttribute", StringComparison.Ordinal))
            {
                return !overridesIsValid;
            }

            if (current.GetMembers("IsValid").OfType<IMethodSymbol>().Any(method => method.IsOverride))
            {
                overridesIsValid = true;
            }
        }

        return false;
    }

    private static bool HasRequiredSatisfyingDefault(
        BindablePropertyCandidate member,
        INamedTypeSymbol rootType,
        Compilation? compilation,
        bool? allowEmptyStringsOverride = null)
    {
        var property = member.Property;

        // RequiredAttribute reads the getter, and a custom getter (including C# field-backed
        // semi-auto getters) can return something other than the initializer or the
        // constructor-assigned value.
        if (!IsAutoImplementedAccessor(property.GetMethod))
        {
            return false;
        }

        var allowEmptyStrings = allowEmptyStringsOverride ?? RequiredAllowsEmptyStrings(property);

        if (member.IsConstructorBound &&
            HasSatisfyingConstructorParameterDefault(property, rootType, allowEmptyStrings))
        {
            return true;
        }

        // A constructor-bound property can also keep a satisfying initializer when no declared
        // constructor overwrites it, so the initializer proof below applies to both shapes.
        var hasSatisfyingInitializer = property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(declaration => declaration.Initializer?.Value is { } value &&
                                InitializerDefinitelySatisfiesRequired(value, property.Type, allowEmptyStrings, compilation));

        // Constructors run after property initializers, so a declared constructor that writes the
        // property (or does anything unprovable) can erase the satisfying default before validation.
        return hasSatisfyingInitializer && NoDeclaredConstructorCanOverwriteProperty(rootType, property);
    }

    private static bool RecursiveDefaultStillFailsValidation(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        bool bindsNonPublicProperties,
        Compilation? compilation,
        bool? recursiveValidationEnabledOverride = null)
    {
        // Recursive validation walks the default instance, so a nested required member without
        // its own satisfying default keeps the parent key required.
        if (!(recursiveValidationEnabledOverride ?? IsRecursiveValidationEnabled(property)))
        {
            return false;
        }

        switch (ClassifyEffectiveRecursiveDefault(rootType, property, compilation))
        {
            case RecursiveDefaultKind.None:
                // Runtime validation skips null members, and empty collection defaults have no
                // items to validate.
                return false;
            case RecursiveDefaultKind.Unprovable:
                return true;
        }

        if (TryGetCollectionElementType(property.Type, out _))
        {
            // A modelled collection default is a clean empty creation, so nothing is validated.
            return false;
        }

        return NestedGraphHasUnsatisfiedRequired(
            property.Type,
            bindsNonPublicProperties,
            compilation,
            new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
    }

    private static bool NestedGraphHasUnsatisfiedRequired(
        ITypeSymbol type,
        bool bindsNonPublicProperties,
        Compilation? compilation,
        HashSet<ITypeSymbol> visited)
    {
        if (!visited.Add(type) ||
            type is not INamedTypeSymbol namedType ||
            !IsPotentialNestedObject(namedType))
        {
            return false;
        }

        // Runtime recursive validation evaluates every DataAnnotations rule on the default
        // instance, not just [Required]; other attributes and IValidatableObject cannot be
        // proven statically, so they keep the ancestor required.
        if (HasTypeLevelValidationInChain(namedType))
        {
            return true;
        }

        var bindableCandidates = new Dictionary<IPropertySymbol, BindablePropertyCandidate>(SymbolEqualityComparer.Default);
        foreach (var candidate in GetBindableProperties(namedType, bindsNonPublicProperties, compilation))
        {
            bindableCandidates[candidate.Property] = candidate;
        }

        // Validator.TryValidateObject(validateAllProperties: true) evaluates every public-getter
        // property, including non-bindable get-only or private-set members.
        foreach (var property in GetValidationVisibleProperties(namedType))
        {
            if (HasNonRequiredValidationAttribute(property))
            {
                return true;
            }

            if (!bindableCandidates.TryGetValue(property, out var candidate))
            {
                candidate = new BindablePropertyCandidate(property, isConstructorBound: false, constructorParameterCanUseDefault: false);
            }

            if (IsRequired(property) &&
                !HasRequiredSatisfyingDefault(candidate, namedType, compilation))
            {
                return true;
            }

            if (IsRecursiveValidationEnabled(property))
            {
                // Runtime recursive validation walks the actual default instance even when the
                // child is not itself required: an unprovable default fails the whole proof,
                // while null members and empty collection defaults are skipped by validation.
                var defaultKind = ClassifyEffectiveRecursiveDefault(namedType, property, compilation);
                if (defaultKind == RecursiveDefaultKind.Unprovable)
                {
                    return true;
                }

                if (defaultKind == RecursiveDefaultKind.Modelled &&
                    !TryGetCollectionElementType(property.Type, out _) &&
                    NestedGraphHasUnsatisfiedRequired(property.Type, bindsNonPublicProperties, compilation, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasTypeLevelValidationInChain(INamedTypeSymbol type)
    {
        if (ImplementsInterface(type, "System.ComponentModel.DataAnnotations.IValidatableObject"))
        {
            return true;
        }

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (HasValidationAttribute(current))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IPropertySymbol> GetValidationVisibleProperties(INamedTypeSymbol type)
    {
        foreach (var property in GetProperties(type))
        {
            if (!property.IsStatic &&
                property.Parameters.Length == 0 &&
                property.DeclaredAccessibility == Accessibility.Public &&
                property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
            {
                yield return property;
            }
        }
    }

    private enum RecursiveDefaultKind
    {
        // The runtime default is null or an empty collection, which validation skips.
        None,
        // The runtime default is a clean, unmutated instance of the declared type.
        Modelled,
        // The runtime default cannot be predicted from the declaration.
        Unprovable
    }

    private static RecursiveDefaultKind ClassifyRecursiveDefault(IPropertySymbol property, Compilation? compilation)
    {
        // Validation reads the getter; a custom getter hides the real default.
        if (!IsAutoImplementedAccessor(property.GetMethod))
        {
            return RecursiveDefaultKind.Unprovable;
        }

        foreach (var reference in property.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration ||
                declaration.Initializer?.Value is not { } initializerValue)
            {
                continue;
            }

            var stripped = StripInitializerWrappers(initializerValue);
            if (IsInitializerDefinitelyNullOrDefault(stripped))
            {
                return RecursiveDefaultKind.None;
            }

            if (stripped is CollectionExpressionSyntax collectionExpression)
            {
                return collectionExpression.Elements.Count == 0
                    ? RecursiveDefaultKind.None
                    : RecursiveDefaultKind.Unprovable;
            }

            return IsCleanDeclaredTypeCreation(stripped, property.Type, compilation)
                ? RecursiveDefaultKind.Modelled
                : RecursiveDefaultKind.Unprovable;
        }

        // No initializer (and the caller has proven no constructor writes the member). A
        // non-nullable value type still has a non-null default(T) instance that DataAnnotations
        // recursively validates — structs are sealed, so it is a clean, unmutated instance of
        // the declared type — so classify it as Modelled. A reference type or Nullable<T> stays
        // null by default, which validation skips.
        return property.Type.IsValueType && !IsNullableValueType(property.Type)
            ? RecursiveDefaultKind.Modelled
            : RecursiveDefaultKind.None;
    }

    private static bool IsCleanDeclaredTypeCreation(
        ExpressionSyntax expression,
        ITypeSymbol declaredType,
        Compilation? compilation)
    {
        return expression switch
        {
            // A polymorphic default is validated as the created type, not the declared one.
            ObjectCreationExpressionSyntax creation =>
                (creation.Initializer is null || creation.Initializer.Expressions.Count == 0) &&
                (creation.ArgumentList is null || creation.ArgumentList.Arguments.Count == 0) &&
                IsInitializerDefinitelyDeclaredType(expression, declaredType, compilation),
            ImplicitObjectCreationExpressionSyntax implicitCreation =>
                (implicitCreation.Initializer is null || implicitCreation.Initializer.Expressions.Count == 0) &&
                implicitCreation.ArgumentList.Arguments.Count == 0,
            _ => false
        };
    }

    private static bool HasNonRequiredValidationAttribute(ISymbol symbol)
    {
        // Exclude only the RequiredAttribute forms IsRequired actually proves (RequiredAttribute
        // itself or a subclass that does not override IsValid): for those the required signal is
        // already handled by IsRequired plus the satisfying-default proof, so counting them here
        // too would keep the key required even when the compile-time default satisfies them. A
        // subclass that overrides IsValid is NOT proven required (IsRequiredAttribute returns
        // false), so it must still count as a validating attribute here — otherwise its property's
        // validation is ignored entirely and a nested-default failure is missed.
        return symbol.GetAttributes().Any(attribute =>
            InheritsFrom(attribute.AttributeClass, "System.ComponentModel.DataAnnotations.ValidationAttribute") &&
            !IsRequiredAttribute(attribute.AttributeClass));
    }

    private static bool NoDeclaredConstructorCanOverwriteProperty(INamedTypeSymbol rootType, IPropertySymbol property)
    {
        return TryResolveRuntimeConstructorEffect(rootType, property, compilation: null, out var assignedKind) &&
               assignedKind is null;
    }

    private static RecursiveDefaultKind ClassifyEffectiveRecursiveDefault(
        INamedTypeSymbol owningType,
        IPropertySymbol property,
        Compilation? compilation)
    {
        if (!TryResolveRuntimeConstructorEffect(owningType, property, compilation, out var assignedKind))
        {
            return RecursiveDefaultKind.Unprovable;
        }

        if (assignedKind is not null)
        {
            // The most-derived definite constructor write is the runtime default — provided the
            // getter actually returns the stored value.
            return IsAutoImplementedAccessor(property.GetMethod)
                ? assignedKind.Value
                : RecursiveDefaultKind.Unprovable;
        }

        return ClassifyRecursiveDefault(property, compilation);
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

    private static RecursiveDefaultKind ClassifyAssignedDefaultValue(
        ExpressionSyntax value,
        ITypeSymbol declaredType,
        Compilation? compilation)
    {
        var stripped = StripInitializerWrappers(value);
        if (IsInitializerDefinitelyNullOrDefault(stripped))
        {
            return RecursiveDefaultKind.None;
        }

        if (stripped is CollectionExpressionSyntax collectionExpression)
        {
            return collectionExpression.Elements.Count == 0
                ? RecursiveDefaultKind.None
                : RecursiveDefaultKind.Unprovable;
        }

        return IsCleanDeclaredTypeCreation(stripped, declaredType, compilation)
            ? RecursiveDefaultKind.Modelled
            : RecursiveDefaultKind.Unprovable;
    }

    private static bool RequiredAllowsEmptyStrings(ISymbol symbol)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            // Match the same attribute IsRequired proved required (RequiredAttribute or a subclass
            // that does not override IsValid). AllowEmptyStrings is an inherited property, so a
            // subclass instance such as [MyRequired(AllowEmptyStrings = true)] carries it too.
            if (!IsRequiredAttribute(attribute.AttributeClass))
            {
                continue;
            }

            return RequiredAllowsEmptyStrings(attribute);
        }

        return false;
    }

    private static bool RequiredAllowsEmptyStrings(AttributeData attribute)
    {
        // A named argument on the usage (e.g. [MyRequired(AllowEmptyStrings = true)]) is the
        // most specific setting — property-initializer setters run after the constructor.
        foreach (var argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.Key, "AllowEmptyStrings", StringComparison.Ordinal) &&
                argument.Value.Value is bool allowEmptyStrings)
            {
                return allowEmptyStrings;
            }
        }

        // Otherwise a subclass may set the inherited AllowEmptyStrings itself (constructor body,
        // constructor parameter, base chaining, ...). Only RequiredAttribute itself has a
        // provable false default with no such possibility.
        if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), "System.ComponentModel.DataAnnotations.RequiredAttribute", StringComparison.Ordinal) &&
            SubclassAllowsEmptyStrings(attribute))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Conservatively decides whether a <c>RequiredAttribute</c> subclass may permit empty strings
    /// via its invoked constructor. Returns a precise value only for a simple, unconditional,
    /// top-level constant assignment (last-wins). Any form the analyzer cannot reduce to a constant
    /// — a non-literal right-hand side (e.g. a constructor parameter), an assignment nested in a
    /// conditional/loop/lambda, or a <c>this</c>/non-RequiredAttribute-<c>base</c> constructor chain —
    /// is treated as "possibly allowed" (returns <c>true</c>) so the analyzer never reports a
    /// runtime-valid empty-string default; it accepts a false negative on those rare shapes instead.
    /// A bare subclass whose (source) constructor provably never sets AllowEmptyStrings returns
    /// <c>false</c>, so the common `[MyRequired] string X { get; set; } = ""` case is still reported.
    /// Known safe-side false negative: a constructor parameter/local that shadows the inherited
    /// AllowEmptyStrings property is matched by name and treated as possibly-allowed.
    /// </summary>
    private static bool SubclassAllowsEmptyStrings(AttributeData attribute)
    {
        // A subclass defined in a referenced assembly has no syntax to inspect, so its constructor
        // could set AllowEmptyStrings out of view — treat it as possibly-allowed. (A source subclass
        // with only an implicit default constructor still falls through to the provable-false result
        // below, because the class itself is in source and its implicit constructor sets nothing.)
        if (attribute.AttributeClass is null || attribute.AttributeClass.DeclaringSyntaxReferences.IsEmpty)
        {
            return true;
        }

        var constructor = attribute.AttributeConstructor;
        if (constructor is null)
        {
            // A syntaxless/implicit constructor sets nothing; the RequiredAttribute default is false.
            return false;
        }

        // The base chain — an explicit base(...) initializer or the implicit base() C# inserts when
        // none is written (including for an implicit/compiler-generated constructor with no syntax) —
        // reaches the direct base type. Only RequiredAttribute itself has a provable no-op base
        // constructor; an intermediate custom subclass base could set AllowEmptyStrings out of view,
        // so treat it as possibly-allowed. This is checked before inspecting the body so it also
        // covers a leaf subclass whose own constructor is implicit.
        if (!string.Equals(constructor.ContainingType.BaseType?.ToDisplayString(), "System.ComponentModel.DataAnnotations.RequiredAttribute", StringComparison.Ordinal))
        {
            return true;
        }

        var sawConstant = false;
        var constantValue = false;

        foreach (var reference in constructor.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not ConstructorDeclarationSyntax declaration)
            {
                // No accessible body to analyze — cannot prove it is not allowed.
                return true;
            }

            // An explicit this(...) initializer runs another overload of the same class, which could
            // set AllowEmptyStrings out of view, so treat it as possibly-allowed.
            if (declaration.Initializer is { } initializer &&
                initializer.ThisOrBaseKeyword.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThisKeyword))
            {
                return true;
            }

            // An expression-bodied constructor (`public MyRequired() => AllowEmptyStrings = true;`)
            // is a single top-level expression.
            if (declaration.ExpressionBody is { } arrow)
            {
                if (!TryTrackConstructorStatement(arrow.Expression, ref sawConstant, ref constantValue))
                {
                    return true;
                }

                continue;
            }

            if (declaration.Body is not { } body)
            {
                // No accessible body/expression — cannot prove it is not allowed.
                return true;
            }

            // The body is provable only if every top-level statement is a simple literal assignment
            // the analyzer fully understands. Anything else — a helper call, a control-flow
            // statement, a local declaration, or an assignment with a non-literal right-hand side —
            // could enable empty strings out of view, so treat it as possibly-allowed.
            foreach (var statement in body.Statements)
            {
                if (statement is not ExpressionStatementSyntax expressionStatement ||
                    !TryTrackConstructorStatement(expressionStatement.Expression, ref sawConstant, ref constantValue))
                {
                    return true;
                }
            }
        }

        return sawConstant && constantValue;
    }

    /// <summary>
    /// Examines a single top-level constructor expression. Returns <c>false</c> when the expression
    /// is anything the analyzer cannot prove leaves AllowEmptyStrings unset — a non-assignment
    /// (e.g. a helper call), an assignment with a non-literal right-hand side, or an assignment to an
    /// AllowEmptyStrings access it cannot resolve — signalling the caller to treat the subclass as
    /// possibly allowing empty strings. A simple `X = &lt;literal&gt;` assignment is understood: it
    /// updates the last-wins constant when X is the inherited AllowEmptyStrings, and is otherwise an
    /// inert write to a different property (a literal has no side effects).
    /// </summary>
    private static bool TryTrackConstructorStatement(ExpressionSyntax? expression, ref bool sawConstant, ref bool constantValue)
    {
        if (expression is not AssignmentExpressionSyntax assignment)
        {
            // A helper call or any non-assignment statement is unprovable.
            return false;
        }

        // A bare `AllowEmptyStrings` or a this./base.-qualified access definitely targets the
        // inherited property.
        var definitelyAllowEmptyStrings = assignment.Left switch
        {
            IdentifierNameSyntax { Identifier.ValueText: "AllowEmptyStrings" } => true,
            MemberAccessExpressionSyntax { Expression: ThisExpressionSyntax or BaseExpressionSyntax, Name.Identifier.ValueText: "AllowEmptyStrings" } => true,
            _ => false
        };

        if (assignment.Right is not LiteralExpressionSyntax literal)
        {
            // A non-literal right-hand side may have side effects or an unprovable value.
            return false;
        }

        if (!definitelyAllowEmptyStrings)
        {
            // Any other member access ending in `AllowEmptyStrings` might still be the inherited
            // property via an alias we cannot resolve without a semantic model, so treat it as
            // unprovable. A literal assignment to any other target is an inert write.
            return assignment.Left is not MemberAccessExpressionSyntax { Name.Identifier.ValueText: "AllowEmptyStrings" };
        }

        if (literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.TrueLiteralExpression))
        {
            sawConstant = true;
            constantValue = true;
            return true;
        }

        if (literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.FalseLiteralExpression))
        {
            sawConstant = true;
            constantValue = false;
            return true;
        }

        return false;
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

    private static bool DefaultValueSatisfiesRequired(object? value, bool allowEmptyStrings)
    {
        if (value is null)
        {
            return false;
        }

        if (value is string text)
        {
            return allowEmptyStrings || text.Trim().Length > 0;
        }

        return true;
    }

    private static bool InitializerDefinitelySatisfiesRequired(
        ExpressionSyntax initializer,
        ITypeSymbol propertyType,
        bool allowEmptyStrings,
        Compilation? compilation)
    {
        initializer = StripInitializerWrappers(initializer);

        // Compile-time constants (literals, const fields, nameof, constant folding) keep their
        // value when the key is missing, so judge them by the constant itself — unless a
        // user-defined conversion decides the stored value instead of the source constant.
        if (compilation is not null)
        {
            var semanticModel = compilation.GetSemanticModel(initializer.SyntaxTree);
            var constantValue = semanticModel.GetConstantValue(initializer);
            if (constantValue.HasValue)
            {
                if (Microsoft.CodeAnalysis.CSharp.CSharpExtensions.ClassifyConversion(semanticModel, initializer, propertyType).IsUserDefined)
                {
                    return false;
                }

                return DefaultValueSatisfiesRequired(constantValue.Value, allowEmptyStrings);
            }

            // A property initializer can reference a primary-constructor parameter directly
            // (e.g. `public string ApiKey { get; set; } = apiKey;` on a primary-constructor
            // class). The parameter itself isn't a compile-time constant at the reference site,
            // but its own explicit default value is, so judge that instead.
            if (initializer is IdentifierNameSyntax identifier &&
                semanticModel.GetSymbolInfo(identifier).Symbol is IParameterSymbol { HasExplicitDefaultValue: true } parameter)
            {
                if (Microsoft.CodeAnalysis.CSharp.CSharpExtensions.ClassifyConversion(semanticModel, initializer, propertyType).IsUserDefined)
                {
                    return false;
                }

                return DefaultValueSatisfiesRequired(parameter.ExplicitDefaultValue, allowEmptyStrings);
            }
        }

        if (initializer is CastExpressionSyntax cast)
        {
            return InitializerDefinitelySatisfiesRequired(cast.Expression, propertyType, allowEmptyStrings, compilation);
        }

        // Signed numeric defaults like -1 parse as a unary expression over a numeric literal.
        if (initializer is PrefixUnaryExpressionSyntax prefixUnary &&
            (prefixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UnaryMinusExpression) ||
             prefixUnary.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.UnaryPlusExpression)) &&
            StripInitializerWrappers(prefixUnary.Operand) is LiteralExpressionSyntax numericOperand &&
            numericOperand.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NumericLiteralExpression))
        {
            return true;
        }

        if (initializer is LiteralExpressionSyntax literal)
        {
            if (literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression) ||
                literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression))
            {
                return false;
            }

            if (literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression))
            {
                return DefaultValueSatisfiesRequired(literal.Token.ValueText, allowEmptyStrings);
            }

            // Numeric, boolean, and character literals are non-null, non-string runtime values,
            // which RequiredAttribute always accepts.
            return true;
        }

        if (initializer is not (ObjectCreationExpressionSyntax or ImplicitObjectCreationExpressionSyntax))
        {
            return false;
        }

        // A constructed string is non-null but can be empty or whitespace, which only
        // AllowEmptyStrings accepts.
        if (propertyType.SpecialType == SpecialType.System_String)
        {
            return allowEmptyStrings;
        }

        // Target-typed new() constructs the property type itself — the underlying value type for
        // nullable value properties per the C# spec — so it always produces a non-null, non-string
        // value here (string properties are excluded above and string has no parameterless constructor).
        if (initializer is ImplicitObjectCreationExpressionSyntax)
        {
            return true;
        }

        // Explicit creations need the semantic constructed type: a type alias can hide Nullable<T>,
        // whose parameterless construction boxes to null, and a constructed string assigned to an
        // object-typed property can still be empty or whitespace.
        if (compilation is null)
        {
            return false;
        }

        var creationSemanticModel = compilation.GetSemanticModel(initializer.SyntaxTree);

        // A user-defined conversion decides the stored value, not the constructed source object.
        if (Microsoft.CodeAnalysis.CSharp.CSharpExtensions.ClassifyConversion(creationSemanticModel, initializer, propertyType).IsUserDefined)
        {
            return false;
        }

        var constructedType = creationSemanticModel.GetTypeInfo(initializer).Type;
        if (constructedType is null)
        {
            return false;
        }

        if (constructedType.SpecialType == SpecialType.System_String)
        {
            return allowEmptyStrings;
        }

        if (IsNullableValueType(constructedType))
        {
            // Only Nullable<T> construction with a value carries HasValue == true.
            return ((ObjectCreationExpressionSyntax)initializer).ArgumentList?.Arguments.Count > 0;
        }

        return true;
    }

    private static bool ContainsValidationAttributes(
        ITypeSymbol type,
        HashSet<ITypeSymbol> visited,
        bool bindsNonPublicProperties,
        Compilation? compilation)
    {
        if (!visited.Add(type))
        {
            return false;
        }

        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        // Reuse the inheritance-aware chain walk CFG002 relies on for the same shape: a
        // type-level ValidationAttribute declared only on a base class is still evaluated by
        // Validator.TryValidateObject (AttributeUsageAttribute.Inherited defaults to true),
        // so checking only the exact type's own attributes would miss it.
        if (HasTypeLevelValidationInChain(namedType))
        {
            return true;
        }

        foreach (var candidate in GetBindableProperties(namedType, bindsNonPublicProperties, compilation))
        {
            var property = candidate.Property;
            if (HasValidationAttribute(property))
            {
                return true;
            }

            if (TryGetCollectionElementType(property.Type, out var elementType))
            {
                if (IsPotentialNestedObject(elementType) &&
                    ContainsValidationAttributes(elementType, visited, bindsNonPublicProperties, compilation))
                {
                    return true;
                }

                continue;
            }

            if (IsPotentialNestedObject(property.Type) &&
                ContainsValidationAttributes(property.Type, visited, bindsNonPublicProperties, compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<IPropertySymbol> GetProperties(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<IPropertySymbol>())
            {
                yield return property;
            }
        }
    }

    private static bool HasAttribute(ISymbol symbol, string metadataName)
    {
        return symbol.GetAttributes().Any(attribute =>
            string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal));
    }

    private static bool InheritsFrom(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.ToDisplayString(), metadataName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsInterface(INamedTypeSymbol type, string metadataName)
    {
        return type.AllInterfaces.Any(iface =>
            string.Equals(iface.ToDisplayString(), metadataName, StringComparison.Ordinal));
    }

}
