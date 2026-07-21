using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

internal sealed partial class OptionsTypeMetadata
{
    internal static bool IsPotentialNestedObject(ITypeSymbol type)
    {
        // The real ConfigurationBinder binds and recurses into struct- and record-struct-typed
        // properties as well as classes. Primitive/BCL value types (int, DateTime, Guid,
        // Nullable<T>, tuples, ...) live in System/System.* and enums are TypeKind.Enum, so
        // both stay excluded by the guards below.
        return (type.TypeKind == TypeKind.Class || type.TypeKind == TypeKind.Struct) &&
               type.SpecialType != SpecialType.System_String &&
               !IsSystemNamespace(type.ContainingNamespace);
    }

    private static bool HasNonNullPropertyInitializer(IPropertySymbol property)
    {
        return property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<PropertyDeclarationSyntax>()
            .Any(declaration => declaration.Initializer?.Value is { } value &&
                                !IsInitializerDefinitelyNullOrDefault(value));
    }

    private static bool HasPotentialPolymorphicInitializer(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        if (property.Type.TypeKind != TypeKind.Class ||
            property.Type.SpecialType == SpecialType.System_String ||
            property.Type is INamedTypeSymbol { IsSealed: true })
        {
            return false;
        }

        foreach (var declaration in property.DeclaringSyntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<PropertyDeclarationSyntax>())
        {
            if (declaration.Initializer?.Value is null)
            {
                continue;
            }

            if (!IsInitializerDefinitelyDeclaredType(declaration.Initializer.Value, property.Type, compilation))
            {
                return true;
            }
        }

        return HasPotentialPolymorphicConstructorAssignment(property, rootType, compilation);
    }

    private static ImmutableHashSet<string> GetPotentialPolymorphicDictionaryValueInitializerKeys(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        Compilation? compilation)
    {
        var keys = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        if (!TryGetDictionaryValueType(property.Type, out _))
        {
            return keys.ToImmutable();
        }

        foreach (var declaration in property.DeclaringSyntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<PropertyDeclarationSyntax>())
        {
            if (declaration.Initializer?.Value is not null)
            {
                AddPotentialPolymorphicDictionaryValueInitializerKeys(
                    keys,
                    declaration.Initializer.Value,
                    property.Type,
                    ImmutableArray<string>.Empty,
                    compilation);
            }
        }

        AddPotentialPolymorphicDictionaryValueConstructorAssignmentKeys(
            keys,
            property,
            rootType,
            property.Type,
            compilation);

        return keys.ToImmutable();
    }

    private static bool CanHavePolymorphicRuntimeValue(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class &&
               type.SpecialType != SpecialType.System_String &&
               type is not INamedTypeSymbol { IsSealed: true };
    }

    private static bool CanHavePolymorphicDictionaryValue(ITypeSymbol dictionaryType)
    {
        return TryGetDictionaryValueType(dictionaryType, out var valueType) &&
               !TryGetDictionaryValueType(valueType, out _) &&
               !TryGetCollectionElementType(valueType, out _) &&
               CanHavePolymorphicRuntimeValue(valueType);
    }

    private static bool IsOpaqueDictionaryInitializer(ExpressionSyntax expression, Compilation? compilation)
    {
        expression = StripInitializerWrappers(expression);
        return expression switch
        {
            ObjectCreationExpressionSyntax { Initializer: null, ArgumentList.Arguments.Count: 0 } => false,
            ImplicitObjectCreationExpressionSyntax { Initializer: null, ArgumentList.Arguments.Count: 0 } => false,
            CollectionExpressionSyntax { Elements.Count: 0 } => false,
            ObjectCreationExpressionSyntax { Initializer: null } objectCreation =>
                !IsEmptyDictionaryConstructorCall(objectCreation, compilation),
            ImplicitObjectCreationExpressionSyntax { Initializer: null } implicitObjectCreation =>
                !IsEmptyDictionaryConstructorCall(implicitObjectCreation, compilation),
            _ => true
        };
    }

    private static bool IsEmptyDictionaryConstructorCall(
        ObjectCreationExpressionSyntax objectCreation,
        Compilation? compilation)
    {
        return IsEmptyDictionaryConstructorCall(
            objectCreation,
            objectCreation.ArgumentList?.Arguments.Count ?? 0,
            compilation);
    }

    private static bool IsEmptyDictionaryConstructorCall(
        ImplicitObjectCreationExpressionSyntax objectCreation,
        Compilation? compilation)
    {
        return IsEmptyDictionaryConstructorCall(
            objectCreation,
            objectCreation.ArgumentList?.Arguments.Count ?? 0,
            compilation);
    }

    private static bool IsEmptyDictionaryConstructorCall(
        ExpressionSyntax objectCreation,
        int argumentCount,
        Compilation? compilation)
    {
        if (argumentCount == 0)
        {
            return true;
        }

        if (compilation is null)
        {
            return false;
        }

        var semanticModel = compilation.GetSemanticModel(objectCreation.SyntaxTree);
        return semanticModel.GetSymbolInfo(objectCreation).Symbol is IMethodSymbol constructor &&
               constructor.Parameters.Length == argumentCount &&
               constructor.Parameters.All(static parameter =>
                   parameter.Type.SpecialType == SpecialType.System_Int32 ||
                   IsEqualityComparerType(parameter.Type));
    }

    private static bool IsEqualityComparerType(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
               namedType.IsGenericType &&
               string.Equals(
                   namedType.OriginalDefinition.ToDisplayString(),
                   "System.Collections.Generic.IEqualityComparer<T>",
                   StringComparison.Ordinal);
    }

    private static void AddPotentialPolymorphicDictionaryValueConstructorAssignmentKeys(
        ImmutableHashSet<string>.Builder keys,
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        Compilation? compilation)
    {
        foreach (var constructor in GetRuntimeConstructorDeclarations(rootType, property, compilation))
        {
            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyAssignment &&
                IsAssignmentToProperty(expressionBodyAssignment, property, compilation))
            {
                AddPotentialPolymorphicDictionaryValueInitializerKeys(
                    keys,
                    expressionBodyAssignment.Right,
                    dictionaryType,
                    ImmutableArray<string>.Empty,
                    compilation);
            }

            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyElementAssignment)
            {
                AddPotentialPolymorphicDictionaryElementAssignmentKey(
                    keys,
                    expressionBodyElementAssignment,
                    property,
                    rootType,
                    dictionaryType,
                    compilation);
            }

            if (constructor.ExpressionBody?.Expression is InvocationExpressionSyntax expressionBodyInvocation)
            {
                AddPotentialPolymorphicDictionaryAddInvocationKey(
                    keys,
                    expressionBodyInvocation,
                    property,
                    rootType,
                    dictionaryType,
                    compilation);
            }

            if (constructor.Body is null)
            {
                continue;
            }

            foreach (var assignment in constructor.Body
                         .DescendantNodes(ShouldDescendIntoConstructorInitializerNode)
                         .OfType<AssignmentExpressionSyntax>())
            {
                if (IsAssignmentToProperty(assignment, property, compilation))
                {
                    AddPotentialPolymorphicDictionaryValueInitializerKeys(
                        keys,
                        assignment.Right,
                        dictionaryType,
                        ImmutableArray<string>.Empty,
                        compilation);
                }

                AddPotentialPolymorphicDictionaryElementAssignmentKey(
                    keys,
                    assignment,
                    property,
                    rootType,
                    dictionaryType,
                    compilation);
            }

            foreach (var invocation in constructor.Body
                         .DescendantNodes(ShouldDescendIntoConstructorInitializerNode)
                         .OfType<InvocationExpressionSyntax>())
            {
                AddPotentialPolymorphicDictionaryAddInvocationKey(
                    keys,
                    invocation,
                    property,
                    rootType,
                    dictionaryType,
                    compilation);
            }
        }
    }

    private static bool CanRuntimeSelectRootConstructor(IMethodSymbol constructor)
    {
        if (constructor.DeclaredAccessibility != Accessibility.Public)
        {
            return false;
        }

        if (constructor.Parameters.Length == 0)
        {
            return true;
        }

        var containingType = constructor.ContainingType;
        if (containingType.InstanceConstructors.Any(static candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public &&
                candidate.Parameters.Length == 0))
        {
            return false;
        }

        var publicParameterizedConstructors = containingType.InstanceConstructors
            .Where(static candidate =>
                candidate.DeclaredAccessibility == Accessibility.Public &&
                candidate.Parameters.Length > 0)
            .ToArray();

        return publicParameterizedConstructors.Length == 1 &&
               SymbolEqualityComparer.Default.Equals(publicParameterizedConstructors[0], constructor) &&
               constructor.Parameters.All(parameter => TryFindMatchingConstructorProperty(containingType, parameter, out _));
    }

    private static void AddPotentialPolymorphicDictionaryElementAssignmentKey(
        ImmutableHashSet<string>.Builder keys,
        AssignmentExpressionSyntax assignment,
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        Compilation? compilation)
    {
        var keyPath = ImmutableArray<string>.Empty;
        if (!assignment.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SimpleAssignmentExpression) ||
            assignment.Left is not ElementAccessExpressionSyntax elementAccess ||
            !TryGetDictionaryElementAccessPath(elementAccess, property, compilation, out keyPath) ||
            assignment.Right.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression))
        {
            return;
        }

        AddPotentialPolymorphicDictionaryValueInitializerKeys(
            keys,
            assignment.Right,
            GetDictionaryValueTypeForPath(dictionaryType, keyPath),
            keyPath,
            compilation,
            DictionaryPathCaseInsensitiveSegments(property, rootType, dictionaryType, keyPath, compilation));
    }

    private static void AddPotentialPolymorphicDictionaryAddInvocationKey(
        ImmutableHashSet<string>.Builder keys,
        InvocationExpressionSyntax invocation,
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        Compilation? compilation)
    {
        if (invocation.Expression is not MemberAccessExpressionSyntax { Name: IdentifierNameSyntax name } memberAccess ||
            !string.Equals(name.Identifier.ValueText, "Add", StringComparison.Ordinal) ||
            invocation.ArgumentList.Arguments.Count < 2 ||
            invocation.ArgumentList.Arguments[1].Expression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression) ||
            !TryGetDictionaryTargetPath(memberAccess.Expression, property, compilation, out var targetPath))
        {
            return;
        }

        var entryPath = AddDictionaryPathSegment(targetPath, invocation.ArgumentList.Arguments[0].Expression, compilation);
        AddPotentialPolymorphicDictionaryValueInitializerKeys(
            keys,
            invocation.ArgumentList.Arguments[1].Expression,
            GetDictionaryValueTypeForPath(dictionaryType, entryPath),
            entryPath,
            compilation,
            DictionaryPathCaseInsensitiveSegments(property, rootType, dictionaryType, entryPath, compilation));
    }

    private static void AddPotentialPolymorphicDictionaryValueInitializerKeys(
        ImmutableHashSet<string>.Builder keys,
        ExpressionSyntax expression,
        ITypeSymbol dictionaryType,
        ImmutableArray<string> keyPath,
        Compilation? compilation,
        ImmutableArray<bool> caseInsensitivePath = default)
    {
        if (!TryGetDictionaryValueType(dictionaryType, out var valueType))
        {
            if (CanHavePolymorphicRuntimeValue(dictionaryType) &&
                !IsInitializerDefinitelyDeclaredType(expression, dictionaryType, compilation))
            {
                AddPotentialPolymorphicDictionaryPath(keys, keyPath, caseInsensitivePath);
            }

            return;
        }

        var initializer = expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Initializer,
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => implicitObjectCreation.Initializer,
            _ => null
        };

        if (initializer is null)
        {
            if (IsOpaqueDictionaryInitializer(expression, compilation) &&
                CanHavePolymorphicDictionaryValue(dictionaryType))
            {
                AddPotentialPolymorphicDictionaryPath(keys, keyPath, caseInsensitivePath);
            }

            return;
        }

        var dictionaryUsesCaseInsensitiveKeys = UsesCaseInsensitiveStringComparer(expression, compilation);
        foreach (var entry in initializer.Expressions)
        {
            if (!TryGetDictionaryInitializerEntry(entry, out var keyExpression, out var valueExpression))
            {
                continue;
            }

            if (valueExpression is null ||
                valueExpression.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.NullLiteralExpression))
            {
                continue;
            }

            var entryPath = AddDictionaryPathSegment(keyPath, keyExpression, compilation);
            var entryCaseInsensitivePath = AddDictionaryCaseInsensitivePathSegment(
                caseInsensitivePath,
                dictionaryUsesCaseInsensitiveKeys);
            if (TryGetDictionaryValueType(valueType, out _))
            {
                AddPotentialPolymorphicDictionaryValueInitializerKeys(
                    keys,
                    valueExpression,
                    valueType,
                    entryPath,
                    compilation,
                    entryCaseInsensitivePath);
                continue;
            }

            if (CanHavePolymorphicRuntimeValue(valueType) &&
                !IsInitializerDefinitelyDeclaredType(valueExpression, valueType, compilation))
            {
                AddPotentialPolymorphicDictionaryPath(keys, entryPath, entryCaseInsensitivePath);
            }
        }
    }

    private static ImmutableArray<bool> AddDictionaryCaseInsensitivePathSegment(
        ImmutableArray<bool> path,
        bool segmentUsesCaseInsensitiveKeys)
    {
        var builder = path.IsDefault
            ? ImmutableArray.CreateBuilder<bool>()
            : path.ToBuilder();
        builder.Add(segmentUsesCaseInsensitiveKeys);
        return builder.ToImmutable();
    }

    private static ImmutableArray<bool> DictionaryPathCaseInsensitiveSegments(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        ImmutableArray<string> keyPath,
        Compilation? compilation)
    {
        if (keyPath.IsDefaultOrEmpty)
        {
            return ImmutableArray<bool>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<bool>(keyPath.Length);
        for (var i = 0; i < keyPath.Length; i++)
        {
            builder.Add(DictionaryPathUsesCaseInsensitiveStringComparer(
                property,
                rootType,
                dictionaryType,
                GetPathPrefix(keyPath, i),
                compilation));
        }

        return builder.ToImmutable();
    }

    private static bool DictionaryPathUsesCaseInsensitiveStringComparer(
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        ImmutableArray<string> dictionaryPath,
        Compilation? compilation)
    {
        foreach (var declaration in property.DeclaringSyntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<PropertyDeclarationSyntax>())
        {
            if (declaration.Initializer?.Value is { } initializer &&
                DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
                    initializer,
                    dictionaryType,
                    dictionaryPath,
                    compilation))
            {
                return true;
            }
        }

        foreach (var constructor in GetRuntimeConstructorDeclarations(rootType, property, compilation))
        {
            if (constructor.ExpressionBody?.Expression is AssignmentExpressionSyntax expressionBodyAssignment &&
                IsAssignmentToProperty(expressionBodyAssignment, property, compilation) &&
                DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
                    expressionBodyAssignment.Right,
                    dictionaryType,
                    dictionaryPath,
                    compilation))
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
                    DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
                        assignment.Right,
                        dictionaryType,
                        dictionaryPath,
                        compilation))
                {
                    return true;
                }

                if (!dictionaryPath.IsDefaultOrEmpty &&
                    assignment.Left is ElementAccessExpressionSyntax elementAccess &&
                    TryGetDictionaryElementAccessPath(elementAccess, property, compilation, out var assignedPath) &&
                    DictionaryPathsEqualForRuntime(
                        assignedPath,
                        dictionaryPath,
                        property,
                        rootType,
                        dictionaryType,
                        compilation) &&
                    UsesCaseInsensitiveStringComparer(assignment.Right, compilation))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool DictionaryPathsEqualForRuntime(
        ImmutableArray<string> left,
        ImmutableArray<string> right,
        IPropertySymbol property,
        INamedTypeSymbol rootType,
        ITypeSymbol dictionaryType,
        Compilation? compilation)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            var parentPath = GetPathPrefix(right, i);
            var ignoreCase = DictionaryPathUsesCaseInsensitiveStringComparer(
                property,
                rootType,
                dictionaryType,
                parentPath,
                compilation);
            if (!DictionaryKeysEqual(left[i], right[i], ignoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static ImmutableArray<string> GetPathPrefix(ImmutableArray<string> path, int length)
    {
        if (length <= 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(length);
        for (var i = 0; i < length; i++)
        {
            builder.Add(path[i]);
        }

        return builder.ToImmutable();
    }

    private static bool DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
        ExpressionSyntax expression,
        ITypeSymbol dictionaryType,
        ImmutableArray<string> dictionaryPath,
        Compilation? compilation)
    {
        if (dictionaryPath.IsDefaultOrEmpty)
        {
            return UsesCaseInsensitiveStringComparer(expression, compilation);
        }

        if (!TryGetDictionaryValueType(dictionaryType, out var valueType))
        {
            return false;
        }

        var initializer = expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.Initializer,
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => implicitObjectCreation.Initializer,
            _ => null
        };

        if (initializer is null)
        {
            return false;
        }

        var dictionaryUsesCaseInsensitiveKeys = UsesCaseInsensitiveStringComparer(expression, compilation);
        foreach (var entry in initializer.Expressions)
        {
            if (!TryGetDictionaryInitializerEntry(entry, out var keyExpression, out var valueExpression) ||
                valueExpression is null ||
                !TryGetConstantString(keyExpression, compilation, out var key) ||
                !DictionaryKeysEqual(key, dictionaryPath[0], dictionaryUsesCaseInsensitiveKeys))
            {
                continue;
            }

            return DictionaryInitializerForPathUsesCaseInsensitiveStringComparer(
                valueExpression,
                valueType,
                RemoveFirstPathSegment(dictionaryPath),
                compilation);
        }

        return false;
    }

    private static bool DictionaryKeysEqual(string left, string right, bool ignoreCase)
    {
        return string.Equals(
            left,
            right,
            ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private static ImmutableArray<string> RemoveFirstPathSegment(ImmutableArray<string> path)
    {
        if (path.Length <= 1)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(path.Length - 1);
        for (var i = 1; i < path.Length; i++)
        {
            builder.Add(path[i]);
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<string> RemoveLastPathSegment(ImmutableArray<string> path)
    {
        if (path.Length <= 1)
        {
            return ImmutableArray<string>.Empty;
        }

        var builder = ImmutableArray.CreateBuilder<string>(path.Length - 1);
        for (var i = 0; i < path.Length - 1; i++)
        {
            builder.Add(path[i]);
        }

        return builder.ToImmutable();
    }

    private static bool TryGetDictionaryInitializerEntry(
        ExpressionSyntax expression,
        out ExpressionSyntax? keyExpression,
        out ExpressionSyntax? valueExpression)
    {
        keyExpression = null;
        valueExpression = null;

        if (expression is AssignmentExpressionSyntax assignment)
        {
            keyExpression = GetDictionaryInitializerAssignmentKeyExpression(assignment.Left);
            valueExpression = assignment.Right;
            return true;
        }

        if (expression is InitializerExpressionSyntax initializer &&
            initializer.Expressions.Count >= 2)
        {
            keyExpression = initializer.Expressions[0];
            valueExpression = initializer.Expressions[1];
            return true;
        }

        if (expression is ObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: >= 2 } objectCreation)
        {
            keyExpression = objectCreation.ArgumentList.Arguments[0].Expression;
            valueExpression = objectCreation.ArgumentList.Arguments[1].Expression;
            return true;
        }

        if (expression is ImplicitObjectCreationExpressionSyntax { ArgumentList.Arguments.Count: >= 2 } implicitObjectCreation)
        {
            keyExpression = implicitObjectCreation.ArgumentList.Arguments[0].Expression;
            valueExpression = implicitObjectCreation.ArgumentList.Arguments[1].Expression;
            return true;
        }

        return false;
    }

    private static ExpressionSyntax? GetDictionaryInitializerAssignmentKeyExpression(ExpressionSyntax expression)
    {
        return expression switch
        {
            ImplicitElementAccessSyntax implicitElementAccess =>
                GetDictionaryElementKeyExpression(implicitElementAccess),
            ElementAccessExpressionSyntax elementAccess =>
                GetDictionaryElementKeyExpression(elementAccess),
            _ => null
        };
    }

    private static ExpressionSyntax? GetDictionaryElementKeyExpression(ElementAccessExpressionSyntax elementAccess)
    {
        return elementAccess.ArgumentList.Arguments.Count > 0
            ? elementAccess.ArgumentList.Arguments[0].Expression
            : null;
    }

    private static ExpressionSyntax? GetDictionaryElementKeyExpression(ImplicitElementAccessSyntax elementAccess)
    {
        return elementAccess.ArgumentList.Arguments.Count > 0
            ? elementAccess.ArgumentList.Arguments[0].Expression
            : null;
    }

    private static ImmutableArray<string> AddDictionaryPathSegment(
        ImmutableArray<string> path,
        ExpressionSyntax? keyExpression,
        Compilation? compilation)
    {
        var builder = path.ToBuilder();
        builder.Add(TryGetConstantString(keyExpression, compilation, out var key)
            ? key
            : BindableProperty.AnyPotentialPolymorphicDictionaryValueKey);
        return builder.ToImmutable();
    }

    private static bool UsesCaseInsensitiveStringComparer(
        ExpressionSyntax expression,
        Compilation? compilation)
    {
        expression = StripInitializerWrappers(expression);
        var arguments = expression switch
        {
            ObjectCreationExpressionSyntax objectCreation => objectCreation.ArgumentList?.Arguments,
            ImplicitObjectCreationExpressionSyntax implicitObjectCreation => implicitObjectCreation.ArgumentList?.Arguments,
            _ => null
        };

        if (arguments is null)
        {
            return false;
        }

        foreach (var argument in arguments.Value)
        {
            if (IsCaseInsensitiveStringComparer(argument.Expression, compilation))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCaseInsensitiveStringComparer(ExpressionSyntax expression, Compilation? compilation)
    {
        return IsCaseInsensitiveStringComparer(
            expression,
            compilation,
            new HashSet<ISymbol>(SymbolEqualityComparer.Default));
    }

    private static bool IsCaseInsensitiveStringComparer(
        ExpressionSyntax expression,
        Compilation? compilation,
        HashSet<ISymbol> visitedSymbols)
    {
        if (compilation is not null)
        {
            var semanticModel = compilation.GetSemanticModel(expression.SyntaxTree);
            var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
            if (symbol is IPropertySymbol property &&
                string.Equals(property.ContainingType.ToDisplayString(), "System.StringComparer", StringComparison.Ordinal) &&
                property.Name.EndsWith("IgnoreCase", StringComparison.Ordinal))
            {
                return true;
            }

            if (symbol is not null &&
                visitedSymbols.Add(symbol) &&
                TryGetCaseInsensitiveStringComparerAliasInitializer(symbol, out var initializer))
            {
                return IsCaseInsensitiveStringComparer(initializer, compilation, visitedSymbols);
            }
        }

        return expression.ToString().Contains("StringComparer.", StringComparison.Ordinal) &&
               expression.ToString().Contains("IgnoreCase", StringComparison.Ordinal);
    }

    private static bool TryGetCaseInsensitiveStringComparerAliasInitializer(
        ISymbol symbol,
        out ExpressionSyntax initializer)
    {
        initializer = null!;
        if (symbol is IFieldSymbol field &&
            IsStringComparerAliasType(field.Type))
        {
            return TryGetVariableInitializer(field.DeclaringSyntaxReferences, out initializer);
        }

        if (symbol is ILocalSymbol local &&
            IsStringComparerAliasType(local.Type))
        {
            return TryGetVariableInitializer(local.DeclaringSyntaxReferences, out initializer);
        }

        if (symbol is IPropertySymbol property &&
            IsStringComparerAliasType(property.Type))
        {
            foreach (var declaration in property.DeclaringSyntaxReferences
                         .Select(reference => reference.GetSyntax())
                         .OfType<PropertyDeclarationSyntax>())
            {
                if (declaration.ExpressionBody?.Expression is { } expressionBody)
                {
                    initializer = expressionBody;
                    return true;
                }

                if (declaration.Initializer?.Value is { } value)
                {
                    initializer = value;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetVariableInitializer(
        ImmutableArray<SyntaxReference> syntaxReferences,
        out ExpressionSyntax initializer)
    {
        initializer = null!;
        foreach (var declaration in syntaxReferences
                     .Select(reference => reference.GetSyntax())
                     .OfType<VariableDeclaratorSyntax>())
        {
            if (declaration.Initializer?.Value is { } value)
            {
                initializer = value;
                return true;
            }
        }

        return false;
    }

    private static bool IsStringComparerAliasType(ITypeSymbol type)
    {
        if (string.Equals(type.ToDisplayString(), "System.StringComparer", StringComparison.Ordinal))
        {
            return true;
        }

        return type is INamedTypeSymbol namedType &&
               namedType.TypeArguments.Length == 1 &&
               namedType.TypeArguments[0].SpecialType == SpecialType.System_String &&
               IsEqualityComparerType(type);
    }

    private static void AddPotentialPolymorphicDictionaryPath(
        ImmutableHashSet<string>.Builder keys,
        ImmutableArray<string> path,
        ImmutableArray<bool> caseInsensitivePath = default)
    {
        if (path.IsDefaultOrEmpty ||
            path.Contains(BindableProperty.AnyPotentialPolymorphicDictionaryValueKey))
        {
            keys.Add(BindableProperty.AnyPotentialPolymorphicDictionaryValueKey);
            return;
        }

        var key = string.Join(":", path);
        keys.Add(key);
        if (!caseInsensitivePath.IsDefaultOrEmpty &&
            caseInsensitivePath.Any(static segment => segment))
        {
            keys.Add(BindableProperty.CaseInsensitivePotentialPolymorphicDictionaryValueKeyPrefix +
                     string.Concat(caseInsensitivePath.Select(static segment => segment ? "1" : "0")) +
                     ":" +
                     key);
        }
    }

    private static bool TryGetDictionaryElementAccessPath(
        ElementAccessExpressionSyntax elementAccess,
        IPropertySymbol property,
        Compilation? compilation,
        out ImmutableArray<string> path)
    {
        if (!TryGetDictionaryTargetPath(elementAccess.Expression, property, compilation, out var targetPath))
        {
            path = ImmutableArray<string>.Empty;
            return false;
        }

        path = AddDictionaryPathSegment(targetPath, GetDictionaryElementKeyExpression(elementAccess), compilation);
        return true;
    }

    private static bool TryGetDictionaryTargetPath(
        ExpressionSyntax expression,
        IPropertySymbol property,
        Compilation? compilation,
        out ImmutableArray<string> path)
    {
        if (IsPropertyAssignmentTarget(expression, property, compilation))
        {
            path = ImmutableArray<string>.Empty;
            return true;
        }

        if (expression is ElementAccessExpressionSyntax elementAccess)
        {
            return TryGetDictionaryElementAccessPath(elementAccess, property, compilation, out path);
        }

        path = ImmutableArray<string>.Empty;
        return false;
    }

    private static ITypeSymbol GetDictionaryValueTypeForPath(
        ITypeSymbol dictionaryType,
        ImmutableArray<string> keyPath)
    {
        var currentType = dictionaryType;
        for (var i = 0; i < keyPath.Length; i++)
        {
            if (!TryGetDictionaryValueType(currentType, out var valueType))
            {
                break;
            }

            currentType = valueType;
        }

        return currentType;
    }

    private static bool TryGetConstantString(
        ExpressionSyntax? expression,
        Compilation? compilation,
        out string key)
    {
        expression = expression is null ? null : StripNullableSuppressions(expression);
        if (expression is null)
        {
            key = null!;
            return false;
        }

        if (compilation is not null)
        {
            var semanticModel = compilation.GetSemanticModel(expression.SyntaxTree);
            var constant = semanticModel.GetConstantValue(expression);
            if (constant.HasValue &&
                constant.Value is string constantKey)
            {
                key = constantKey;
                return true;
            }
        }

        if (expression is LiteralExpressionSyntax literal &&
            literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression) &&
            literal.Token.Value is string literalKey)
        {
            key = literalKey;
            return true;
        }

        key = null!;
        return false;
    }

}
