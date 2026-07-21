using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ConfigContraband;

public sealed partial class ConfigContrabandAnalyzer
{
    private static void AnalyzeGetValueRead(
        SyntaxNodeAnalysisContext syntaxContext,
        DirectConfigurationInvocation invocation,
        ConfigurationSnapshot configuration,
        ConcurrentDictionary<string, byte> configurationDiagnosticsReported)
    {
        if (invocation.KeyExpression is not { } keyExpression ||
            invocation.TargetType is not { } targetType ||
            !invocation.ArgumentsAreProvablySafe ||
            !TryGetConfigurationSectionPath(
                invocation.Receiver,
                keyExpression,
                syntaxContext.SemanticModel,
                out var fullPath,
                out _,
                out _) ||
            ClassifyConfigurationReceiver(invocation.Receiver, syntaxContext.SemanticModel) !=
                ConfigurationReceiverProvenance.Contract)
        {
            return;
        }

        foreach (var property in configuration.FindProperties(fullPath))
        {
            if (!ScalarConversion.IsProvablyNotConvertible(
                    targetType,
                    property.ScalarKind,
                    property.ScalarValue))
            {
                continue;
            }

            var location = property.ValueLocation ?? property.Location;
            var reportKey = CreateConfigurationValueTypeMismatchReportKey(
                targetType,
                location,
                property.FullPath);
            if (!configurationDiagnosticsReported.TryAdd(reportKey, 0))
            {
                continue;
            }

            syntaxContext.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.ConfigurationValueTypeMismatch,
                location,
                property.FullPath,
                targetType.ToDisplayString()));
        }
    }

    private static bool HasCompileTimeConstantValue(IOperation operation)
    {
        while (operation is IConversionOperation { OperatorMethod: null } conversion)
        {
            operation = conversion.Operand;
        }

        return operation.ConstantValue.HasValue;
    }

    private static bool IsProvablySafeBinderInstanceArgument(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation switch
        {
            ILocalReferenceOperation => true,
            IParameterReferenceOperation => true,
            IInstanceReferenceOperation => true,
            IFieldReferenceOperation field =>
                field.Instance is not null && IsProvablySafeBinderInstanceArgument(field.Instance),
            ILiteralOperation => true,
            IDefaultValueOperation => true,
            IObjectCreationOperation creation =>
                creation.Constructor?.IsImplicitlyDeclared == true &&
                creation.Arguments.IsEmpty &&
                creation.Initializer is null &&
                IsProvablySideEffectFreeImplicitConstruction(creation.Constructor.ContainingType),
            _ => false,
        };
    }

    private static bool IsProvablySideEffectFreeImplicitConstruction(INamedTypeSymbol type)
    {
        if (type.TypeKind != TypeKind.Class ||
            type.BaseType?.SpecialType != SpecialType.System_Object ||
            type.DeclaringSyntaxReferences.IsEmpty ||
            type.StaticConstructors.Any(constructor => !constructor.IsImplicitlyDeclared))
        {
            return false;
        }

        foreach (var member in type.GetMembers())
        {
            if (member.IsImplicitlyDeclared)
            {
                continue;
            }

            ITypeSymbol? memberType = member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                IEventSymbol @event => @event.Type,
                _ => null,
            };
            if (memberType is null)
            {
                continue;
            }

            foreach (var syntaxReference in member.DeclaringSyntaxReferences)
            {
                ExpressionSyntax? initializer = syntaxReference.GetSyntax() switch
                {
                    VariableDeclaratorSyntax variable => variable.Initializer?.Value,
                    PropertyDeclarationSyntax property => property.Initializer?.Value,
                    _ => null,
                };
                if (initializer is not null &&
                    !IsProvablySideEffectFreeInitializer(initializer, memberType))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool IsProvablySideEffectFreeInitializer(
        ExpressionSyntax expression,
        ITypeSymbol targetType)
    {
        return expression switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.DefaultLiteralExpression) => true,
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression) =>
                targetType.IsReferenceType ||
                targetType is INamedTypeSymbol
                {
                    OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                },
            LiteralExpressionSyntax => IsSafeStandardLiteralTarget(targetType),
            TypeOfExpressionSyntax =>
                targetType.SpecialType == SpecialType.System_Object ||
                string.Equals(targetType.ToDisplayString(), "System.Type", StringComparison.Ordinal),
            ParenthesizedExpressionSyntax parenthesized =>
                IsProvablySideEffectFreeInitializer(parenthesized.Expression, targetType),
            PrefixUnaryExpressionSyntax
            {
                Operand: LiteralExpressionSyntax,
            } => IsSafeStandardLiteralTarget(targetType),
            PostfixUnaryExpressionSyntax postfix when postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression) =>
                IsProvablySideEffectFreeInitializer(postfix.Operand, targetType),
            InvocationExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.Text: "nameof" },
            } => targetType.SpecialType is SpecialType.System_String or SpecialType.System_Object,
            _ => false,
        };
    }

    private static bool IsSafeStandardLiteralTarget(ITypeSymbol targetType)
    {
        if (targetType.SpecialType != SpecialType.None || targetType.TypeKind == TypeKind.Enum)
        {
            return true;
        }

        return targetType is INamedTypeSymbol namedType &&
               namedType.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T &&
               namedType.TypeArguments[0] is { } underlyingType &&
               (underlyingType.SpecialType != SpecialType.None || underlyingType.TypeKind == TypeKind.Enum);
    }

    private static bool IsProvablySafeBinderOptionsArgument(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        if (operation is IDelegateCreationOperation delegateCreation)
        {
            operation = delegateCreation.Target;
        }

        if (operation is not IAnonymousFunctionOperation function ||
            function.Symbol.Parameters.Length != 1)
        {
            return false;
        }

        var parameter = function.Symbol.Parameters[0];
        foreach (var bodyOperation in function.Body.Operations)
        {
            if (bodyOperation is IReturnOperation { ReturnedValue: null })
            {
                continue;
            }

            if (bodyOperation is not IExpressionStatementOperation
                {
                    Operation: ISimpleAssignmentOperation
                    {
                        Target: IPropertyReferenceOperation property,
                        Value: var value,
                    },
                } ||
                property.Instance is not IParameterReferenceOperation parameterReference ||
                !SymbolEqualityComparer.Default.Equals(parameterReference.Parameter, parameter) ||
                !string.Equals(
                    property.Property.ContainingType?.ToDisplayString(),
                    "Microsoft.Extensions.Configuration.BinderOptions",
                    StringComparison.Ordinal) ||
                !value.ConstantValue.HasValue)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsFrameworkGenericGetValueMethod(
        IMethodSymbol originalMethod,
        IMethodSymbol targetMethod)
    {
        if (!IsFrameworkConfigurationBinderMethod(originalMethod) ||
            !string.Equals(originalMethod.Name, "GetValue", StringComparison.Ordinal) ||
            !targetMethod.IsGenericMethod ||
            targetMethod.TypeArguments.Length != 1 ||
            originalMethod.Arity != 1 ||
            originalMethod.Parameters.Length is < 2 or > 3)
        {
            return false;
        }

        var configurationParameter = originalMethod.Parameters[0];
        var keyParameter = originalMethod.Parameters[1];
        return string.Equals(
                   configurationParameter.Type.ToDisplayString(),
                   "Microsoft.Extensions.Configuration.IConfiguration",
                   StringComparison.Ordinal) &&
               string.Equals(keyParameter.Name, "key", StringComparison.Ordinal) &&
               keyParameter.Type.SpecialType == SpecialType.System_String;
    }

    private static bool TryGetFrameworkNonGenericGetValueTargetType(
        IMethodSymbol originalMethod,
        IInvocationOperation operation,
        out ITypeSymbol? targetType)
    {
        targetType = null;
        if (!IsFrameworkConfigurationBinderMethod(originalMethod) ||
            !string.Equals(originalMethod.Name, "GetValue", StringComparison.Ordinal) ||
            originalMethod.Arity != 0 ||
            originalMethod.Parameters.Length is < 3 or > 4)
        {
            return false;
        }

        var configurationParameter = originalMethod.Parameters[0];
        var typeParameter = originalMethod.Parameters[1];
        var keyParameter = originalMethod.Parameters[2];
        if (!string.Equals(
                configurationParameter.Type.ToDisplayString(),
                "Microsoft.Extensions.Configuration.IConfiguration",
                StringComparison.Ordinal) ||
            !string.Equals(typeParameter.Name, "type", StringComparison.Ordinal) ||
            !string.Equals(typeParameter.Type.ToDisplayString(), "System.Type", StringComparison.Ordinal) ||
            !string.Equals(keyParameter.Name, "key", StringComparison.Ordinal) ||
            keyParameter.Type.SpecialType != SpecialType.System_String)
        {
            return false;
        }

        var typeArgument = operation.Arguments.FirstOrDefault(argument =>
            string.Equals(argument.Parameter?.Name, "type", StringComparison.Ordinal));
        if (typeArgument is null)
        {
            return false;
        }

        IOperation typeValue = typeArgument.Value;
        while (typeValue is IConversionOperation { OperatorMethod: null } conversion)
        {
            typeValue = conversion.Operand;
        }

        if (typeValue is not ITypeOfOperation typeOfOperation)
        {
            return false;
        }

        targetType = typeOfOperation.TypeOperand;
        return true;
    }

    private static bool IsFrameworkConfigurationExtensionsMethod(IMethodSymbol method)
    {
        if (!string.Equals(
                method.ContainingType?.ToDisplayString(),
                "Microsoft.Extensions.Configuration.ConfigurationExtensions",
                StringComparison.Ordinal) ||
            !string.Equals(
                method.ContainingAssembly.Identity.Name,
                "Microsoft.Extensions.Configuration.Abstractions",
                StringComparison.Ordinal) ||
            method.Parameters.Length == 0 ||
            !string.Equals(
                method.Parameters[0].Type.ToDisplayString(),
                "Microsoft.Extensions.Configuration.IConfiguration",
                StringComparison.Ordinal))
        {
            return false;
        }

        var extensionsPublicKeyToken = method.ContainingAssembly.Identity.PublicKeyToken;
        var configurationPublicKeyToken = method.Parameters[0].Type.ContainingAssembly.Identity.PublicKeyToken;
        return !extensionsPublicKeyToken.IsDefaultOrEmpty &&
               extensionsPublicKeyToken.SequenceEqual(configurationPublicKeyToken);
    }

    private static bool IsFrameworkConfigurationBinderMethod(IMethodSymbol method)
    {
        if (!string.Equals(
                method.ContainingType?.ToDisplayString(),
                "Microsoft.Extensions.Configuration.ConfigurationBinder",
                StringComparison.Ordinal) ||
            !string.Equals(
                method.ContainingAssembly.Identity.Name,
                "Microsoft.Extensions.Configuration.Binder",
                StringComparison.Ordinal) ||
            method.Parameters.Length == 0 ||
            !string.Equals(
                method.Parameters[0].Type.ToDisplayString(),
                "Microsoft.Extensions.Configuration.IConfiguration",
                StringComparison.Ordinal))
        {
            return false;
        }

        var binderPublicKeyToken = method.ContainingAssembly.Identity.PublicKeyToken;
        var configurationPublicKeyToken = method.Parameters[0].Type.ContainingAssembly.Identity.PublicKeyToken;
        return !binderPublicKeyToken.IsDefaultOrEmpty &&
               binderPublicKeyToken.SequenceEqual(configurationPublicKeyToken);
    }
}
