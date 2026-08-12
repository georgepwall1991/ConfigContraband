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

        if (!TryGetTryAddEnumerableValidator(operation, out implementation, out optionsType))
        {
            return false;
        }

        return IsFrameworkOptionsValidator(implementation, optionsType);
    }

    public static bool IsServiceCollectionRegistration(
        InvocationExpressionSyntax invocation,
        SemanticModel model)
    {
        if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method)
        {
            return false;
        }

        return IsServiceCollectionRegistration(method);
    }

    public static bool IsServiceCollectionRegistration(IMethodSymbol method)
    {
        var original = GetUnreducedOriginal(method);
        if (IsFrameworkAddSingleton(original))
        {
            return true;
        }

        return IsFrameworkTryAddEnumerable(original);
    }

    public static bool SameServiceCollectionOrUnproven(
        InvocationExpressionSyntax left,
        InvocationExpressionSyntax right,
        SemanticModel model)
    {
        if (!TryGetServiceCollectionRoot(left, model, out var leftCollection))
        {
            return true;
        }

        if (!TryGetServiceCollectionRoot(right, model, out var rightCollection))
        {
            return true;
        }

        return SymbolEqualityComparer.Default.Equals(leftCollection, rightCollection);
    }

    private static bool TryGetAddSingletonValidator(
        IInvocationOperation operation,
        out INamedTypeSymbol implementation,
        out INamedTypeSymbol optionsType)
    {
        implementation = null!;
        optionsType = null!;
        var original = GetUnreducedOriginal(operation.TargetMethod);
        if (!IsFrameworkAddSingleton(original))
        {
            return false;
        }

        if (original.Parameters.Length != 1)
        {
            return false;
        }

        return TryGetValidateOptionsTypeArguments(operation.TargetMethod, out implementation, out optionsType);
    }

    private static bool TryGetTryAddEnumerableValidator(
        IInvocationOperation operation,
        out INamedTypeSymbol implementation,
        out INamedTypeSymbol optionsType)
    {
        implementation = null!;
        optionsType = null!;
        var original = GetUnreducedOriginal(operation.TargetMethod);
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

            var descriptorOriginal = GetUnreducedOriginal(descriptorInvocation.TargetMethod);
            if (!IsFrameworkServiceDescriptorSingleton(descriptorOriginal))
            {
                continue;
            }

            if (descriptorOriginal.Parameters.Length != 0)
            {
                continue;
            }

            if (TryGetValidateOptionsTypeArguments(
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
        if (method.TypeArguments.Length != 2)
        {
            return false;
        }

        if (method.TypeArguments[0] is not INamedTypeSymbol serviceType)
        {
            return false;
        }

        if (method.TypeArguments[1] is not INamedTypeSymbol implementationType)
        {
            return false;
        }

        if (!IsFrameworkValidateOptions(serviceType, out optionsType))
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
            if (IsFrameworkOptionsValidatorAttribute(attribute.AttributeClass!))
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
            if (!IsFrameworkValidateOptions(candidate, out var validatedType))
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(validatedType, optionsType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFrameworkValidateOptions(INamedTypeSymbol type, out INamedTypeSymbol optionsType)
    {
        optionsType = null!;
        if (type.Name != "IValidateOptions")
        {
            return false;
        }

        if (type.TypeArguments.Length != 1)
        {
            return false;
        }

        if (type.TypeArguments[0] is not INamedTypeSymbol argument)
        {
            return false;
        }

        if (type.ContainingNamespace.ToDisplayString() != "Microsoft.Extensions.Options")
        {
            return false;
        }

        if (type.ContainingAssembly.Name != "Microsoft.Extensions.Options")
        {
            return false;
        }

        optionsType = argument;
        return true;
    }

    private static bool IsFrameworkOptionsValidatorAttribute(INamedTypeSymbol attributeClass)
    {
        if (attributeClass.ToDisplayString() != "Microsoft.Extensions.Options.OptionsValidatorAttribute")
        {
            return false;
        }

        return attributeClass.ContainingAssembly.Name == "Microsoft.Extensions.Options";
    }

    private static bool IsFrameworkAddSingleton(IMethodSymbol method)
    {
        if (method.Name != "AddSingleton")
        {
            return false;
        }

        return method.ContainingType.ToDisplayString() ==
               "Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions";
    }

    private static bool IsFrameworkTryAddEnumerable(IMethodSymbol method)
    {
        if (method.Name != "TryAddEnumerable")
        {
            return false;
        }

        return method.ContainingType.ToDisplayString() ==
               "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions";
    }

    private static bool IsFrameworkServiceDescriptorSingleton(IMethodSymbol method)
    {
        if (method.Name != "Singleton")
        {
            return false;
        }

        return method.ContainingType.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.ServiceDescriptor";
    }

    private static IMethodSymbol GetUnreducedOriginal(IMethodSymbol method)
    {
        if (method.ReducedFrom is { } reduced)
        {
            return reduced.OriginalDefinition;
        }

        return method.OriginalDefinition;
    }

    private static IOperation UnwrapConversion(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            if (conversion.OperatorMethod is not null)
            {
                break;
            }

            operation = conversion.Operand;
        }

        return operation;
    }

    private static bool TryGetServiceCollectionRoot(
        InvocationExpressionSyntax invocation,
        SemanticModel model,
        out ISymbol collection)
    {
        collection = null!;
        var current = invocation;
        while (true)
        {
            if (current.Expression is not MemberAccessExpressionSyntax memberAccess)
            {
                return false;
            }

            if (memberAccess.Expression is InvocationExpressionSyntax receiverInvocation)
            {
                current = receiverInvocation;
                continue;
            }

            var receiver = model.GetSymbolInfo(memberAccess.Expression).Symbol;
            if (receiver is ILocalSymbol)
            {
                collection = receiver;
                return true;
            }

            if (receiver is IParameterSymbol)
            {
                collection = receiver;
                return true;
            }

            return false;
        }
    }
}
