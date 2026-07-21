using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

internal sealed partial class OptionsTypeMetadata
{
    private static bool HasNonNullRuntimeInitializer(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        return HasNonNullPropertyInitializer(property) ||
               HasNonNullConstructorAssignment(property, rootType, compilation);
    }

    private static bool HasNonNullConstructorAssignment(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        foreach (var constructor in GetRuntimeConstructorDeclarations(rootType, property, compilation))
        {
            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyAssignment &&
                IsAssignmentToProperty(expressionBodyAssignment, property, compilation) &&
                !IsInitializerDefinitelyNullOrDefault(expressionBodyAssignment.Right))
            {
                return true;
            }

            if (constructor.Body is null)
            {
                continue;
            }

            foreach (var assignment in GetDefinitelyExecutedConstructorAssignments(constructor))
            {
                if (IsAssignmentToProperty(assignment, property, compilation) &&
                    !IsInitializerDefinitelyNullOrDefault(assignment.Right))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<AssignmentExpressionSyntax> GetDefinitelyExecutedConstructorAssignments(
        ConstructorDeclarationSyntax constructor)
    {
        if (constructor.Body is null)
        {
            yield break;
        }

        var hasPriorConditionalExit = false;
        foreach (var statement in constructor.Body.Statements)
        {
            if (!hasPriorConditionalExit &&
                statement is ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment })
            {
                yield return assignment;
            }

            if (ContainsConstructorExit(statement))
            {
                hasPriorConditionalExit = true;
            }
        }
    }

    private static bool ContainsConstructorExit(SyntaxNode node)
    {
        return node.DescendantNodesAndSelf(ShouldDescendIntoConstructorInitializerNode).Any(static descendant =>
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ReturnStatement) ||
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GotoStatement) ||
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GotoCaseStatement) ||
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.GotoDefaultStatement) ||
            descendant.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ThrowStatement));
    }

    private static bool HasPotentialPolymorphicConstructorAssignment(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        foreach (var constructor in GetRuntimeConstructorDeclarations(rootType, property, compilation))
        {
            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyAssignment &&
                IsPotentialPolymorphicAssignmentToProperty(expressionBodyAssignment, property, compilation))
            {
                return true;
            }

            if (constructor.Body is null)
            {
                continue;
            }

            foreach (var assignment in constructor.Body
                         .DescendantNodes(ShouldDescendIntoConstructorInitializerNode)
                         .OfType<AssignmentExpressionSyntax>())
            {
                if (IsPotentialPolymorphicAssignmentToProperty(assignment, property, compilation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ShouldDescendIntoConstructorInitializerNode(SyntaxNode node)
    {
        return node is not AnonymousFunctionExpressionSyntax and
               not LocalFunctionStatementSyntax;
    }

    private static IEnumerable<ConstructorDeclarationSyntax> GetRuntimeConstructorDeclarations(
        INamedTypeSymbol rootType,
        IPropertySymbol property,
        Compilation? compilation)
    {
        if (compilation is not null)
        {
            var constructors = ImmutableArray.CreateBuilder<ConstructorDeclarationSyntax>();
            var visited = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
            foreach (var constructor in rootType.InstanceConstructors.Where(CanRuntimeSelectRootConstructor))
            {
                AddReachableConstructorDeclarations(constructor, constructors, visited, compilation);
            }

            foreach (var constructor in constructors)
            {
                yield return constructor;
            }

            yield break;
        }

        for (INamedTypeSymbol? current = rootType; current is not null; current = current.BaseType)
        {
            foreach (var declaration in current.DeclaringSyntaxReferences
                         .Select(reference => reference.GetSyntax())
                         .OfType<TypeDeclarationSyntax>())
            {
                foreach (var constructor in declaration.Members.OfType<ConstructorDeclarationSyntax>())
                {
                    yield return constructor;
                }
            }

            if (SymbolEqualityComparer.Default.Equals(current, property.ContainingType))
            {
                yield break;
            }
        }
    }

    private static void AddReachableConstructorDeclarations(
        IMethodSymbol constructor,
        ImmutableArray<ConstructorDeclarationSyntax>.Builder constructors,
        HashSet<IMethodSymbol> visited,
        Compilation compilation)
    {
        if (!visited.Add(constructor))
        {
            return;
        }

        var declaration = GetConstructorDeclaration(constructor);
        if (declaration is not null)
        {
            constructors.Add(declaration);
        }

        var chainedConstructor = declaration?.Initializer is { } initializer
            ? GetChainedConstructor(initializer, compilation)
            : GetImplicitBaseConstructor(constructor);

        if (chainedConstructor is not null)
        {
            AddReachableConstructorDeclarations(chainedConstructor, constructors, visited, compilation);
        }
    }

    private static ConstructorDeclarationSyntax? GetConstructorDeclaration(IMethodSymbol constructor)
    {
        return constructor.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault();
    }

    private static IMethodSymbol? GetChainedConstructor(
        ConstructorInitializerSyntax initializer,
        Compilation compilation)
    {
        var semanticModel = compilation.GetSemanticModel(initializer.SyntaxTree);
        return semanticModel.GetSymbolInfo(initializer).Symbol as IMethodSymbol;
    }

    private static IMethodSymbol? GetImplicitBaseConstructor(IMethodSymbol constructor)
    {
        if (constructor.ContainingType.BaseType is not { SpecialType: not SpecialType.System_Object } baseType)
        {
            return null;
        }

        return baseType.InstanceConstructors.FirstOrDefault(static candidate => candidate.Parameters.Length == 0);
    }

    private static bool IsPotentialPolymorphicAssignmentToProperty(
        AssignmentExpressionSyntax assignment,
        IPropertySymbol property,
        Compilation? compilation)
    {
        return IsAssignmentToProperty(assignment, property, compilation) &&
               !IsInitializerDefinitelyDeclaredType(assignment.Right, property.Type, compilation);
    }

    private static bool IsAssignmentToProperty(
        AssignmentExpressionSyntax assignment,
        IPropertySymbol property,
        Compilation? compilation)
    {
        return assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) &&
               IsPropertyAssignmentTarget(assignment.Left, property, compilation);
    }

    private static bool IsPropertyAssignmentTarget(
        ExpressionSyntax expression,
        IPropertySymbol property,
        Compilation? compilation)
    {
        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            if (memberAccess.Expression is not ThisExpressionSyntax &&
                memberAccess.Expression is not BaseExpressionSyntax)
            {
                return false;
            }

            if (compilation is null &&
                memberAccess.Name is IdentifierNameSyntax name)
            {
                return string.Equals(name.Identifier.ValueText, property.Name, StringComparison.Ordinal);
            }
        }

        if (compilation is null)
        {
            return false;
        }

        var semanticModel = compilation.GetSemanticModel(expression.SyntaxTree);
        return SymbolEqualityComparer.Default.Equals(
            semanticModel.GetSymbolInfo(expression).Symbol,
            property);
    }

    private static bool IsInitializerDefinitelyDeclaredType(
        ExpressionSyntax initializer,
        ITypeSymbol declaredType,
        Compilation? compilation)
    {
        initializer = StripInitializerWrappers(initializer);
        if (IsInitializerDefinitelyNullOrDefault(initializer))
        {
            return true;
        }

        return initializer switch
        {
            ImplicitObjectCreationExpressionSyntax => true,
            ObjectCreationExpressionSyntax objectCreation => IsTypeSyntaxDeclaredType(objectCreation.Type, declaredType, compilation),
            _ => false
        };
    }

    private static bool IsInitializerDefinitelyNullOrDefault(ExpressionSyntax initializer)
    {
        initializer = StripInitializerWrappers(initializer);
        if (initializer is CastExpressionSyntax cast)
        {
            return IsInitializerDefinitelyNullOrDefault(cast.Expression);
        }

        return initializer.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression) ||
               initializer.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression) ||
               initializer is DefaultExpressionSyntax;
    }

    private static ExpressionSyntax StripInitializerWrappers(ExpressionSyntax expression)
    {
        while (true)
        {
            expression = StripNullableSuppressions(expression);
            if (expression is ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
                continue;
            }

            return expression;
        }
    }

    private static ExpressionSyntax StripNullableSuppressions(ExpressionSyntax expression)
    {
        while (expression is PostfixUnaryExpressionSyntax postfix &&
               postfix.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SuppressNullableWarningExpression))
        {
            expression = postfix.Operand;
        }

        return expression;
    }

    private static bool IsTypeSyntaxDeclaredType(
        TypeSyntax typeSyntax,
        ITypeSymbol declaredType,
        Compilation? compilation)
    {
        if (compilation is not null)
        {
            var semanticModel = compilation.GetSemanticModel(typeSyntax.SyntaxTree);
            var type = semanticModel.GetTypeInfo(typeSyntax).Type;
            if (type is null && typeSyntax is NameSyntax nameSyntax)
            {
                type = semanticModel.GetAliasInfo(nameSyntax)?.Target as ITypeSymbol;
            }

            if (type is not null)
            {
                return IsSameType(type, declaredType);
            }

            return IsQualifiedTypeSyntaxDeclaredType(typeSyntax, declaredType);
        }

        return typeSyntax switch
        {
            IdentifierNameSyntax identifier => string.Equals(identifier.Identifier.ValueText, declaredType.Name, StringComparison.Ordinal),
            GenericNameSyntax generic => string.Equals(generic.Identifier.ValueText, declaredType.Name, StringComparison.Ordinal),
            QualifiedNameSyntax or AliasQualifiedNameSyntax => IsQualifiedTypeSyntaxDeclaredType(typeSyntax, declaredType),
            NullableTypeSyntax nullable => IsTypeSyntaxDeclaredType(nullable.ElementType, declaredType, compilation),
            _ => false
        };
    }

    private static bool IsQualifiedTypeSyntaxDeclaredType(TypeSyntax typeSyntax, ITypeSymbol declaredType)
    {
        var syntaxName = NormalizeTypeSyntaxName(typeSyntax.ToString());
        var displayName = NormalizeTypeSyntaxName(declaredType.ToDisplayString());
        var fullyQualifiedName = NormalizeTypeSyntaxName(declaredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

        return string.Equals(syntaxName, displayName, StringComparison.Ordinal) ||
               string.Equals(syntaxName, fullyQualifiedName, StringComparison.Ordinal);
    }

    private static bool IsSameType(ITypeSymbol type, ITypeSymbol declaredType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, declaredType))
        {
            return true;
        }

        return string.Equals(
            NormalizeTypeSyntaxName(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
            NormalizeTypeSyntaxName(declaredType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)),
            StringComparison.Ordinal);
    }

    private static string NormalizeTypeSyntaxName(string name)
    {
        const string globalPrefix = "global::";
        return name.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? name.Substring(globalPrefix.Length)
            : name;
    }

}
