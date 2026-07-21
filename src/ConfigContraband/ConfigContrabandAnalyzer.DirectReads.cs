using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace ConfigContraband;

public sealed partial class ConfigContrabandAnalyzer
{
    private enum DirectConfigurationApiKind
    {
        GetRequiredSection,
        GetConnectionString,
        Get,
        GetValue,
        Bind,
    }

    private readonly struct DirectConfigurationInvocation
    {
        public DirectConfigurationInvocation(
            DirectConfigurationApiKind kind,
            IMethodSymbol originalMethod,
            ExpressionSyntax receiver,
            ExpressionSyntax? keyExpression,
            ITypeSymbol? targetType,
            bool argumentsAreProvablySafe,
            InvocationExpressionSyntax syntax)
        {
            Kind = kind;
            OriginalMethod = originalMethod;
            Receiver = receiver;
            KeyExpression = keyExpression;
            TargetType = targetType;
            ArgumentsAreProvablySafe = argumentsAreProvablySafe;
            Syntax = syntax;
        }

        public DirectConfigurationApiKind Kind { get; }
        public IMethodSymbol OriginalMethod { get; }
        public ExpressionSyntax Receiver { get; }
        public ExpressionSyntax? KeyExpression { get; }
        public ITypeSymbol? TargetType { get; }
        public bool ArgumentsAreProvablySafe { get; }
        public InvocationExpressionSyntax Syntax { get; }
    }

    private static void AnalyzeDirectConfigurationRead(
        SyntaxNodeAnalysisContext syntaxContext,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics,
        ConcurrentDictionary<string, byte> configurationDiagnosticsReported)
    {
        var invocation = (InvocationExpressionSyntax)syntaxContext.Node;
        if (syntaxContext.SemanticModel.GetOperation(invocation, syntaxContext.CancellationToken) is not IInvocationOperation operation ||
            !TryNormalizeDirectConfigurationInvocation(operation, out var directInvocation))
        {
            return;
        }

        switch (directInvocation.Kind)
        {
            case DirectConfigurationApiKind.GetRequiredSection:
                AnalyzeStandaloneRequiredSectionRead(
                    syntaxContext,
                    directInvocation,
                    configuration,
                    providerSemantics);
                break;
            case DirectConfigurationApiKind.GetConnectionString:
                AnalyzeConnectionStringRead(
                    syntaxContext,
                    directInvocation,
                    configuration,
                    providerSemantics);
                break;
            case DirectConfigurationApiKind.Get:
            case DirectConfigurationApiKind.Bind:
                AnalyzeBoundSectionRead(
                    syntaxContext,
                    directInvocation,
                    configuration,
                    providerSemantics);
                break;
            case DirectConfigurationApiKind.GetValue:
                AnalyzeGetValueRead(
                    syntaxContext,
                    directInvocation,
                    configuration,
                    configurationDiagnosticsReported);
                break;
        }
    }

    private static bool TryNormalizeDirectConfigurationInvocation(
        IInvocationOperation operation,
        out DirectConfigurationInvocation invocation)
    {
        invocation = default;
        if (operation.Syntax is not InvocationExpressionSyntax syntax)
        {
            return false;
        }

        var originalMethod = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod;
        var containingType = originalMethod.ContainingType?.ToDisplayString();
        DirectConfigurationApiKind kind;
        string? keyParameterName = null;
        ITypeSymbol? targetType = null;
        var argumentsAreProvablySafe = true;

        if (string.Equals(containingType, "Microsoft.Extensions.Configuration.ConfigurationExtensions", StringComparison.Ordinal))
        {
            if (!IsFrameworkConfigurationExtensionsMethod(originalMethod))
            {
                return false;
            }

            if (string.Equals(originalMethod.Name, "GetRequiredSection", StringComparison.Ordinal))
            {
                kind = DirectConfigurationApiKind.GetRequiredSection;
                keyParameterName = "key";
            }
            else if (string.Equals(originalMethod.Name, "GetConnectionString", StringComparison.Ordinal))
            {
                kind = DirectConfigurationApiKind.GetConnectionString;
                keyParameterName = "name";
            }
            else
            {
                return false;
            }
        }
        else if (string.Equals(containingType, "Microsoft.Extensions.Configuration.ConfigurationBinder", StringComparison.Ordinal))
        {
            if (!IsFrameworkConfigurationBinderMethod(originalMethod))
            {
                return false;
            }

            if (string.Equals(originalMethod.Name, "Get", StringComparison.Ordinal))
            {
                kind = DirectConfigurationApiKind.Get;
                var configureOptionsArgument = operation.Arguments.FirstOrDefault(argument =>
                    string.Equals(argument.Parameter?.Name, "configureOptions", StringComparison.Ordinal));
                if (configureOptionsArgument is not null)
                {
                    argumentsAreProvablySafe = IsProvablySafeBinderOptionsArgument(configureOptionsArgument.Value);
                }
            }
            else if (IsFrameworkGenericGetValueMethod(originalMethod, operation.TargetMethod))
            {
                kind = DirectConfigurationApiKind.GetValue;
                keyParameterName = "key";
                targetType = operation.TargetMethod.TypeArguments[0];
                if (originalMethod.Parameters.Any(parameter =>
                        string.Equals(parameter.Name, "defaultValue", StringComparison.Ordinal)))
                {
                    argumentsAreProvablySafe = operation.Arguments.Any(argument =>
                        string.Equals(argument.Parameter?.Name, "defaultValue", StringComparison.Ordinal) &&
                        HasCompileTimeConstantValue(argument.Value));
                }
            }
            else if (TryGetFrameworkNonGenericGetValueTargetType(originalMethod, operation, out targetType))
            {
                kind = DirectConfigurationApiKind.GetValue;
                keyParameterName = "key";
                if (originalMethod.Parameters.Any(parameter =>
                        string.Equals(parameter.Name, "defaultValue", StringComparison.Ordinal)))
                {
                    argumentsAreProvablySafe = operation.Arguments.Any(argument =>
                        string.Equals(argument.Parameter?.Name, "defaultValue", StringComparison.Ordinal) &&
                        HasCompileTimeConstantValue(argument.Value));
                }
            }
            else if (string.Equals(originalMethod.Name, "Bind", StringComparison.Ordinal))
            {
                var instanceArgument = operation.Arguments.FirstOrDefault(argument =>
                    string.Equals(argument.Parameter?.Name, "instance", StringComparison.Ordinal));
                argumentsAreProvablySafe = instanceArgument is not null &&
                    IsProvablySafeBinderInstanceArgument(instanceArgument.Value);

                var configureOptionsArgument = operation.Arguments.FirstOrDefault(argument =>
                    string.Equals(argument.Parameter?.Name, "configureOptions", StringComparison.Ordinal));
                if (configureOptionsArgument is not null)
                {
                    argumentsAreProvablySafe = argumentsAreProvablySafe &&
                        IsProvablySafeBinderOptionsArgument(configureOptionsArgument.Value);
                }

                if (originalMethod.Parameters.Any(parameter =>
                        string.Equals(parameter.Name, "key", StringComparison.Ordinal) &&
                        parameter.Type.SpecialType == SpecialType.System_String))
                {
                    keyParameterName = "key";
                }

                kind = DirectConfigurationApiKind.Bind;
            }
            else
            {
                return false;
            }
        }
        else
        {
            return false;
        }

        ExpressionSyntax? receiver = null;
        if (operation.TargetMethod.ReducedFrom is not null)
        {
            receiver = operation.Instance?.Syntax as ExpressionSyntax;
        }
        else
        {
            foreach (var argument in operation.Arguments)
            {
                if (argument.Parameter?.Ordinal == 0 && argument.Value.Syntax is ExpressionSyntax receiverExpression)
                {
                    receiver = receiverExpression;
                    break;
                }
            }
        }

        if (receiver is null)
        {
            return false;
        }

        ExpressionSyntax? keyExpression = null;
        if (keyParameterName is not null)
        {
            foreach (var argument in operation.Arguments)
            {
                if (string.Equals(argument.Parameter?.Name, keyParameterName, StringComparison.Ordinal) &&
                    argument.Value.Syntax is ExpressionSyntax argumentExpression)
                {
                    keyExpression = argumentExpression;
                    break;
                }
            }

            if (keyExpression is null)
            {
                return false;
            }
        }

        invocation = new DirectConfigurationInvocation(
            kind,
            originalMethod,
            receiver,
            keyExpression,
            targetType,
            argumentsAreProvablySafe,
            syntax);
        return true;
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

    private static void AnalyzeStandaloneRequiredSectionRead(
        SyntaxNodeAnalysisContext syntaxContext,
        DirectConfigurationInvocation invocation,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        var semanticModel = syntaxContext.SemanticModel;
        if (invocation.KeyExpression is not { } keyExpression ||
            !TryGetConfigurationSectionPath(
                invocation.Receiver,
                keyExpression,
                semanticModel,
                out var sectionPath,
                out var sectionExpression,
                out var sectionExpressionContainsFullPath) ||
            configuration.GetSectionExistence(sectionPath, providerSemantics) != ConfigurationSectionExistence.Missing ||
            IsOptionsRegistrationSectionRead(invocation.Syntax, semanticModel) ||
            ClassifyConfigurationReceiver(invocation.Receiver, semanticModel) !=
                ConfigurationReceiverProvenance.Contract ||
            ChainContainsMissingRequiredParent(
                invocation.Receiver,
                semanticModel,
                configuration,
                providerSemantics))
        {
            return;
        }

        ReportMissingSection(
            syntaxContext.ReportDiagnostic,
            DiagnosticDescriptors.ConfigurationKeyNotFound,
            sectionPath,
            sectionExpression,
            sectionExpressionContainsFullPath,
            configuration);
    }

    private static void AnalyzeBoundSectionRead(
        SyntaxNodeAnalysisContext syntaxContext,
        DirectConfigurationInvocation invocation,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        var semanticModel = syntaxContext.SemanticModel;
        var provenanceReceiver = invocation.Receiver;
        bool hasSectionPath;
        string sectionPath;
        ExpressionSyntax sectionExpression;
        bool sectionExpressionContainsFullPath;
        if (invocation.KeyExpression is { } conditionalKeyExpression &&
            TryGetDeepConditionalSectionPath(
                invocation.Syntax,
                semanticModel,
                out var conditionalPrefix,
                out _,
                out _,
                out var conditionalKeyRoot) &&
            TryGetConstantSectionPath(conditionalKeyExpression, semanticModel, out var conditionalKey))
        {
            hasSectionPath = true;
            sectionPath = conditionalPrefix + ":" + conditionalKey;
            sectionExpression = conditionalKeyExpression;
            sectionExpressionContainsFullPath = false;
            provenanceReceiver = conditionalKeyRoot;
        }
        else if (invocation.KeyExpression is { } keyExpression)
        {
            hasSectionPath = TryGetConfigurationSectionPath(
                invocation.Receiver,
                keyExpression,
                semanticModel,
                out sectionPath,
                out sectionExpression,
                out sectionExpressionContainsFullPath);
        }
        else if (TryGetDeepConditionalSectionPath(
                     invocation.Syntax,
                     semanticModel,
                     out sectionPath,
                     out sectionExpression,
                     out sectionExpressionContainsFullPath,
                     out var conditionalRoot))
        {
            hasSectionPath = true;
            provenanceReceiver = conditionalRoot;
        }
        else
        {
            hasSectionPath = TryGetConfigurationSectionPath(
                invocation.Receiver,
                semanticModel,
                out sectionPath,
                out sectionExpression,
                out sectionExpressionContainsFullPath);
        }

        if (!hasSectionPath ||
            !invocation.ArgumentsAreProvablySafe ||
            configuration.GetSectionExistence(sectionPath, providerSemantics) != ConfigurationSectionExistence.Missing ||
            IsOptionsRegistrationSectionRead(invocation.Syntax, semanticModel) ||
            ClassifyConfigurationReceiver(provenanceReceiver, semanticModel) !=
                ConfigurationReceiverProvenance.Contract ||
            ChainContainsMissingRequiredParent(
                invocation.Receiver,
                semanticModel,
                configuration,
                providerSemantics))
        {
            return;
        }

        ReportMissingSection(
            syntaxContext.ReportDiagnostic,
            DiagnosticDescriptors.ConfigurationKeyNotFound,
            sectionPath,
            sectionExpression,
            sectionExpressionContainsFullPath,
            configuration,
            requireSuggestion: true);
    }

    private static bool TryGetDeepConditionalSectionPath(
        InvocationExpressionSyntax consumer,
        SemanticModel semanticModel,
        out string sectionPath,
        out ExpressionSyntax sectionExpression,
        out bool sectionExpressionContainsFullPath,
        out ExpressionSyntax rootReceiver)
    {
        sectionPath = null!;
        sectionExpression = null!;
        sectionExpressionContainsFullPath = false;
        rootReceiver = null!;

        var links = new List<(string Path, ExpressionSyntax Expression)>();
        foreach (var conditionalAccess in consumer.Ancestors().OfType<ConditionalAccessExpressionSyntax>())
        {
            if (conditionalAccess.Expression is InvocationExpressionSyntax sectionInvocation &&
                sectionInvocation.Expression is MemberBindingExpressionSyntax memberBinding)
            {
                if (!string.Equals(memberBinding.Name.Identifier.ValueText, "GetSection", StringComparison.Ordinal) ||
                    sectionInvocation.ArgumentList.Arguments.Count != 1 ||
                    !IsFrameworkConfigurationGetSectionInvocation(sectionInvocation, semanticModel))
                {
                    return false;
                }

                var keyExpression = sectionInvocation.ArgumentList.Arguments[0].Expression;
                if (!TryGetConstantSectionPath(keyExpression, semanticModel, out var key))
                {
                    return false;
                }

                links.Add((key, keyExpression));
                continue;
            }

            rootReceiver = UnwrapForSectionChainResolution(conditionalAccess.Expression);
            break;
        }

        if (links.Count == 0 || rootReceiver is null)
        {
            return false;
        }

        links.Reverse();
        var conditionalPath = string.Join(":", links.Select(static link => link.Path));
        if (TryGetConfigurationSectionPath(
                rootReceiver,
                semanticModel,
                out var ordinaryPrefix,
                out _,
                out _))
        {
            sectionPath = ordinaryPrefix + ":" + conditionalPath;
            rootReceiver = GetConfigurationChainRoot(rootReceiver, semanticModel);
        }
        else
        {
            if (IsConfigurationSectionType(semanticModel.GetTypeInfo(rootReceiver).Type) ||
                !IsConfigurationType(semanticModel.GetTypeInfo(rootReceiver).Type))
            {
                return false;
            }

            sectionPath = conditionalPath;
        }

        sectionExpression = links[links.Count - 1].Expression;
        return true;
    }

    private static void AnalyzeConnectionStringRead(
        SyntaxNodeAnalysisContext syntaxContext,
        DirectConfigurationInvocation invocation,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        if (invocation.KeyExpression is not { } nameExpression)
        {
            return;
        }

        var semanticModel = syntaxContext.SemanticModel;
        if (!TryGetConstantSectionPath(nameExpression, semanticModel, out var connectionName))
        {
            return;
        }

        string? prefix = null;
        if (TryGetConfigurationSectionPath(invocation.Receiver, semanticModel, out var receiverPath, out _, out _))
        {
            prefix = receiverPath;
        }
        else
        {
            var receiverType = semanticModel.GetTypeInfo(invocation.Receiver).Type;
            if (IsConfigurationSectionType(receiverType) || !IsConfigurationType(receiverType))
            {
                return;
            }
        }

        var connectionStringsPath = prefix is null ? "ConnectionStrings" : prefix + ":ConnectionStrings";
        var fullPath = connectionStringsPath + ":" + connectionName;
        if (configuration.FindSections(connectionStringsPath).IsDefaultOrEmpty ||
            configuration.GetSectionExistence(fullPath, providerSemantics) != ConfigurationSectionExistence.Missing ||
            ClassifyConfigurationReceiver(invocation.Receiver, semanticModel) !=
                ConfigurationReceiverProvenance.Contract)
        {
            return;
        }

        // Connection strings are routinely supplied by environment variables or secret
        // stores, so a plain "name not in appsettings" signal is too weak on its own.
        // Only report when the name is a near-miss of a connection string that IS
        // declared in appsettings — a provable typo.
        ReportMissingSection(
            syntaxContext.ReportDiagnostic,
            DiagnosticDescriptors.ConfigurationKeyNotFound,
            fullPath,
            nameExpression,
            sectionExpressionContainsFullPath: false,
            configuration,
            requireSuggestion: true);
    }

    /// <summary>
    /// Determines whether the direct read feeds a recognized options registration in the
    /// same statement (for example `services.Configure&lt;T&gt;(config.GetRequiredSection("X"))`).
    /// Those reads are already covered by CFG001 through the registration itself, so
    /// reporting CFG009 as well would double-report the same missing section.
    /// </summary>
    private static bool IsOptionsRegistrationSectionRead(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        foreach (var candidate in invocation.Ancestors().OfType<InvocationExpressionSyntax>())
        {
            if (!TryCreateRegistration(candidate, semanticModel, out var registration))
            {
                continue;
            }

            var sectionArgument = candidate.ArgumentList.Arguments.FirstOrDefault(argument =>
                argument.Span.Contains(registration.SectionExpression.Span));
            if (sectionArgument is not null && sectionArgument.Expression.Span.Contains(invocation.Span))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Walks the receiver chain of a direct-read invocation looking for an earlier
    /// `GetRequiredSection` link whose own resolved path is already missing. That inner
    /// link produces its own CFG009, so the outer read stays quiet instead of cascading
    /// a second diagnostic for the same root cause.
    /// </summary>
    private static bool ChainContainsMissingRequiredParent(
        ExpressionSyntax receiver,
        SemanticModel semanticModel,
        ConfigurationSnapshot configuration,
        ConfigurationProviderSemantics providerSemantics)
    {
        ExpressionSyntax? expression = receiver;

        while (expression is not null)
        {
            expression = UnwrapForSectionChainResolution(expression);
            if (expression is ConditionalAccessExpressionSyntax conditionalAccess)
            {
                expression = conditionalAccess.Expression;
                continue;
            }

            if (expression is not InvocationExpressionSyntax parentInvocation ||
                parentInvocation.Expression is not MemberAccessExpressionSyntax parentMemberAccess ||
                !IsConfigurationSectionMethodName(parentMemberAccess.Name.Identifier.ValueText))
            {
                return false;
            }

            if (string.Equals(parentMemberAccess.Name.Identifier.ValueText, "GetRequiredSection", StringComparison.Ordinal) &&
                semanticModel.GetSymbolInfo(parentInvocation).Symbol is IMethodSymbol parentMethod &&
                string.Equals((parentMethod.ReducedFrom ?? parentMethod).ContainingType?.ToDisplayString(), "Microsoft.Extensions.Configuration.ConfigurationExtensions", StringComparison.Ordinal) &&
                TryGetConfigurationSectionPath(parentInvocation, semanticModel, out var parentPath, out _, out _) &&
                configuration.GetSectionExistence(parentPath, providerSemantics) == ConfigurationSectionExistence.Missing)
            {
                return true;
            }

            expression = parentMemberAccess.Expression;
        }

        return false;
    }

}
