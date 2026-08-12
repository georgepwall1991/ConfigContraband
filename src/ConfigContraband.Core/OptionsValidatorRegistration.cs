using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace ConfigContraband;

/// <summary>
/// Proves the first-party <c>[OptionsValidator]</c> source-generator registration shape:
/// <c>AddSingleton&lt;IValidateOptions&lt;T&gt;, TImpl&gt;()</c> or
/// <c>TryAddEnumerable(ServiceDescriptor.Singleton&lt;IValidateOptions&lt;T&gt;, TImpl&gt;())</c>
/// where <c>TImpl</c> carries the framework <c>[OptionsValidator]</c> attribute.
/// </summary>
internal static class OptionsValidatorRegistration
{
    public static bool TryGetValidatedOptionsType(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out INamedTypeSymbol optionsType)
    {
        optionsType = null!;
        if (model.GetOperation(invocation) is not IInvocationOperation operation)
        {
            return false;
        }

        if (TryGetAddSingletonValidator(operation, out var implementation, out optionsType))
        {
            return IsFrameworkOptionsValidator(implementation, optionsType);
        }

        return TryGetTryAddEnumerableValidator(operation, out implementation, out optionsType) &&
               IsFrameworkOptionsValidator(implementation, optionsType);
    }

    public static bool IsServiceCollectionRegistration(IMethodSymbol method)
    {
        var original = method.ReducedFrom ?? method.OriginalDefinition;
        return IsFrameworkAddSingleton(original) || IsFrameworkTryAddEnumerable(original);
    }

    private static bool TryGetAddSingletonValidator(
        IInvocationOperation operation,
        out INamedTypeSymbol implementation,
        out INamedTypeSymbol optionsType)
    {
        implementation = null!;
        optionsType = null!;
        var original = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod.OriginalDefinition;
        if (!IsFrameworkAddSingleton(original) ||
            original.Parameters.Length != 1 ||
            !TryGetValidateOptionsTypeArguments(operation.TargetMethod, out implementation, out optionsType))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetTryAddEnumerableValidator(
        IInvocationOperation operation,
        out INamedTypeSymbol implementation,
        out INamedTypeSymbol optionsType)
    {
        implementation = null!;
        optionsType = null!;
        var original = operation.TargetMethod.ReducedFrom ?? operation.TargetMethod.OriginalDefinition;
        if (!IsFrameworkTryAddEnumerable(original))
        {
            return false;
        }

        foreach (var argument in operation.Arguments)
        {
            if (UnwrapConversion(argument.Value) is not IInvocationOperation descriptorInvocation)
            {
                continue;
            }

            var descriptorOriginal = descriptorInvocation.TargetMethod.ReducedFrom ??
                                     descriptorInvocation.TargetMethod.OriginalDefinition;
            if (IsFrameworkServiceDescriptorSingleton(descriptorOriginal) &&
                descriptorOriginal.Parameters.Length == 0 &&
                TryGetValidateOptionsTypeArguments(
                    descriptorInvocation.TargetMethod,
                    out implementation,
                    out optionsType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetValidateOptionsTypeArguments(
        IMethodSymbol method,
        out INamedTypeSymbol implementation,
        out INamedTypeSymbol optionsType)
    {
        implementation = null!;
        optionsType = null!;
        if (method.TypeArguments.Length != 2 ||
            method.TypeArguments[0] is not INamedTypeSymbol serviceType ||
            method.TypeArguments[1] is not INamedTypeSymbol implementationType ||
            !IsFrameworkValidateOptions(serviceType, out optionsType))
        {
            return false;
        }

        implementation = implementationType;
        return true;
    }

    private static bool IsFrameworkOptionsValidator(INamedTypeSymbol implementation, INamedTypeSymbol optionsType)
    {
        if (!ImplementsFrameworkValidateOptions(implementation, optionsType))
        {
            return false;
        }

        foreach (var attribute in implementation.GetAttributes())
        {
            if (IsFrameworkOptionsValidatorAttribute(attribute.AttributeClass))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsFrameworkValidateOptions(INamedTypeSymbol implementation, INamedTypeSymbol optionsType)
    {
        foreach (var candidate in implementation.AllInterfaces)
        {
            if (IsFrameworkValidateOptions(candidate, out var validatedType) &&
                SymbolEqualityComparer.Default.Equals(validatedType, optionsType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFrameworkValidateOptions(INamedTypeSymbol type, out INamedTypeSymbol optionsType)
    {
        optionsType = null!;
        if (type.Name != "IValidateOptions" ||
            type.TypeArguments.Length != 1 ||
            type.TypeArguments[0] is not INamedTypeSymbol argument ||
            type.ContainingNamespace?.ToDisplayString() != "Microsoft.Extensions.Options" ||
            type.ContainingAssembly?.Name != "Microsoft.Extensions.Options")
        {
            return false;
        }

        optionsType = argument;
        return HasNonEmptyPublicKeyToken(type.ContainingAssembly);
    }

    private static bool IsFrameworkOptionsValidatorAttribute(INamedTypeSymbol? attributeClass)
    {
        return attributeClass is not null &&
               attributeClass.ToDisplayString() == "Microsoft.Extensions.Options.OptionsValidatorAttribute" &&
               attributeClass.ContainingAssembly?.Name == "Microsoft.Extensions.Options" &&
               HasNonEmptyPublicKeyToken(attributeClass.ContainingAssembly);
    }

    private static bool IsFrameworkAddSingleton(IMethodSymbol method)
    {
        return method.Name == "AddSingleton" &&
               method.ContainingType?.ToDisplayString() ==
                   "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions" &&
               (method.ContainingAssembly?.Name == "Microsoft.Extensions.DependencyInjection.Abstractions" ||
                method.ContainingAssembly?.Name == "Microsoft.Extensions.DependencyInjection") &&
               HasNonEmptyPublicKeyToken(method.ContainingAssembly);
    }

    private static bool IsFrameworkTryAddEnumerable(IMethodSymbol method)
    {
        return method.Name == "TryAddEnumerable" &&
               method.ContainingType?.ToDisplayString() ==
                   "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions" &&
               (method.ContainingAssembly?.Name == "Microsoft.Extensions.DependencyInjection.Abstractions" ||
                method.ContainingAssembly?.Name == "Microsoft.Extensions.DependencyInjection") &&
               HasNonEmptyPublicKeyToken(method.ContainingAssembly);
    }

    private static bool IsFrameworkServiceDescriptorSingleton(IMethodSymbol method)
    {
        return method.Name == "Singleton" &&
               method.ContainingType?.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.ServiceDescriptor" &&
               (method.ContainingAssembly?.Name == "Microsoft.Extensions.DependencyInjection.Abstractions" ||
                method.ContainingAssembly?.Name == "Microsoft.Extensions.DependencyInjection") &&
               HasNonEmptyPublicKeyToken(method.ContainingAssembly);
    }

    private static bool HasNonEmptyPublicKeyToken(IAssemblySymbol? assembly)
    {
        return assembly is not null && !assembly.Identity.PublicKeyToken.IsDefaultOrEmpty;
    }

    private static IOperation UnwrapConversion(IOperation operation)
    {
        while (operation is IConversionOperation { OperatorMethod: null } conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }
}
