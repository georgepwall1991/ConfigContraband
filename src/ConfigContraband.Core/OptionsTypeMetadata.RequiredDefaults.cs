using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ConfigContraband;

internal sealed partial class OptionsTypeMetadata
{
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
}
