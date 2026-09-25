using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// Recognition and emission of user-defined rules: attributes deriving from
/// <c>ZeroAlloc.Validation.ValidationAttribute&lt;T&gt;</c>. Each usage is rebuilt once as a
/// static instance of the attribute and checked with a statically bound <c>IsValid</c> call.
/// </summary>
internal static class CustomRules
{
    private const string ValidationAttributeOfTMetadataName = "ValidationAttribute`1";
    private const string ValidationAttributeMetadataName = "ValidationAttribute";
    private const string ValidationNamespace = "ZeroAlloc.Validation";
    private const string RuleMessageAttributeMetadataName = "RuleMessageAttribute";

    /// <summary>
    /// Returns <c>T</c> of the first <c>ValidationAttribute&lt;T&gt;</c> in the base-type chain of
    /// <paramref name="attrClass"/>, which is closed for any attribute that can be applied.
    /// </summary>
    public static bool TryGetRuleValueType(INamedTypeSymbol attrClass, out ITypeSymbol valueType)
    {
        for (var current = attrClass.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.MetadataName, ValidationAttributeOfTMetadataName, StringComparison.Ordinal)
                && string.Equals(current.ContainingNamespace?.ToDisplayString(), ValidationNamespace, StringComparison.Ordinal))
            {
                valueType = current.TypeArguments[0];
                return true;
            }
        }

        valueType = null!;
        return false;
    }

    /// <summary>Whether <paramref name="attr"/> is a user-defined rule, a <c>ValidationAttribute&lt;T&gt;</c> subclass.</summary>
    public static bool IsCustomRule(AttributeData attr) =>
        attr.AttributeClass is { } attrClass && TryGetRuleValueType(attrClass, out _);

    /// <summary>
    /// Whether <paramref name="attrClass"/> derives, directly or indirectly, from the non-generic
    /// <c>ZeroAlloc.Validation.ValidationAttribute</c>.
    /// </summary>
    public static bool DerivesFromValidationAttribute(INamedTypeSymbol attrClass)
    {
        for (var current = attrClass.BaseType; current is not null; current = current.BaseType)
        {
            if (string.Equals(current.MetadataName, ValidationAttributeMetadataName, StringComparison.Ordinal)
                && string.Equals(current.ContainingNamespace?.ToDisplayString(), ValidationNamespace, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The first symbol the rule's field initializer names that the generated validator, a
    /// separate class in the same assembly, cannot access, or <see langword="null"/> when every
    /// one is accessible. Checked in order: the attribute type, each type containing it and each
    /// type argument of a generic rule, the constructor the usage binds, each member a named argument sets, and every type an argument
    /// value names, the operand of a <c>typeof</c> or the type of an enum constant. A
    /// <c>file</c>-local type counts as inaccessible, because the generated validator is declared
    /// in another file.
    /// </summary>
    public static ISymbol? FindInaccessibleSymbol(Compilation compilation, AttributeData attr)
    {
        var assembly = compilation.Assembly;
        return FindNamedSymbol(attr, symbol =>
            symbol is INamedTypeSymbol { IsFileLocal: true }
            || !compilation.IsSymbolAccessibleWithin(symbol, assembly));
    }

    /// <summary>
    /// Whether the rule's field initializer names an <c>[Obsolete]</c> symbol: the attribute type,
    /// a type containing it or a type argument of a generic rule, the constructor, a member a named argument sets, or a type an
    /// argument value names. The compiler would warn about it inside generated code, where the
    /// user cannot suppress it, although it already warns at the usage in user code.
    /// </summary>
    public static bool NamesObsoleteSymbol(AttributeData attr) =>
        FindNamedSymbol(attr, IsObsolete) is not null;

    /// <summary>
    /// The first symbol, in the order <see cref="FindInaccessibleSymbol"/> documents, that the
    /// rule's field initializer names and <paramref name="matches"/> accepts.
    /// </summary>
    private static ISymbol? FindNamedSymbol(AttributeData attr, Func<ISymbol, bool> matches)
    {
        var attrClass = attr.AttributeClass!;

        // The attribute type is written in full, so its containing types and the type arguments
        // of a generic rule such as [Rule<Secret>] are named too.
        if (FindNamedType(attrClass, matches) is { } foundType)
            return foundType;

        if (attr.AttributeConstructor is { } ctor && matches(ctor))
            return ctor;

        // Only members the initializer assigns are named; the base members are read at compile
        // time instead. For accessibility this is defensive: C# accepts a named argument only for
        // a public read-write field or property with a public setter, CS0617, so the check can
        // fail only through an inaccessible containing type, which the loop above already
        // rejects. It stays so that the rule never depends on that language restriction.
        foreach (var named in attr.NamedArguments)
        {
            if (IsBaseMemberArgument(attrClass, named.Key)) continue;
            var member = FindNamedArgumentMember(attrClass, named.Key);
            if (member is not null && matches(member))
                return member;
        }

        foreach (var argument in attr.ConstructorArguments)
        {
            if (FindNamedType(argument, matches) is { } found)
                return found;
        }

        foreach (var named in attr.NamedArguments)
        {
            if (FindNamedType(named.Value, matches) is { } found)
                return found;
        }

        return null;
    }

    /// <summary>
    /// The first type <see cref="RenderConstant"/> writes for <paramref name="constant"/> that
    /// <paramref name="matches"/> accepts: a <c>typeof</c> operand, an enum constant's cast
    /// target, and the same for each element of an array.
    /// </summary>
    private static ITypeSymbol? FindNamedType(TypedConstant constant, Func<ISymbol, bool> matches)
    {
        if (constant.IsNull) return null;

        switch (constant.Kind)
        {
            case TypedConstantKind.Array:
                foreach (var element in constant.Values)
                {
                    if (FindNamedType(element, matches) is { } found)
                        return found;
                }
                return null;
            case TypedConstantKind.Type:
                return constant.Value is ITypeSymbol operand ? FindNamedType(operand, matches) : null;
            case TypedConstantKind.Enum:
                return constant.Type is null ? null : FindNamedType(constant.Type, matches);
            default:
                return null;
        }
    }

    /// <summary>
    /// The innermost part of <paramref name="type"/> that <paramref name="matches"/> accepts: a
    /// containing type, a type argument of a constructed generic, recursively, or the type
    /// itself. Parts are checked before the whole, so <c>List&lt;Secret&gt;</c> reports
    /// <c>Secret</c> rather than the constructed list.
    /// </summary>
    private static ITypeSymbol? FindNamedType(ITypeSymbol type, Func<ISymbol, bool> matches)
    {
        switch (type)
        {
            case IArrayTypeSymbol array:
                return FindNamedType(array.ElementType, matches);
            case IPointerTypeSymbol pointer:
                return FindNamedType(pointer.PointedAtType, matches);
            case ITypeParameterSymbol:
                // The placeholder of an unbound generic such as typeof(List<>); nothing is named.
                return null;
            case INamedTypeSymbol named:
                if (named.ContainingType is { } containing
                    && FindNamedType(containing, matches) is { } foundContainer)
                {
                    return foundContainer;
                }
                if (!named.IsUnboundGenericType)
                {
                    foreach (var typeArgument in named.TypeArguments)
                    {
                        if (FindNamedType(typeArgument, matches) is { } foundArgument)
                            return foundArgument;
                    }
                }
                return matches(named) ? named : null;
            default:
                return matches(type) ? type : null;
        }
    }

    /// <summary>
    /// Whether <paramref name="symbol"/> is <c>[Obsolete]</c>. A property setter, which is what
    /// a named argument binds, is judged by its property, where the attribute is written.
    /// </summary>
    private static bool IsObsolete(ISymbol symbol) =>
        HasObsoleteAttribute(symbol)
        || (symbol is IMethodSymbol { AssociatedSymbol: { } associated } && HasObsoleteAttribute(associated));

    private static bool HasObsoleteAttribute(ISymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            if (string.Equals(attr.AttributeClass?.ToDisplayString(), "System.ObsoleteAttribute", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Whether the named argument <paramref name="name"/>, written on a usage of
    /// <paramref name="attrClass"/>, sets one of the members declared on
    /// <c>ZeroAlloc.Validation.ValidationAttribute</c> itself: <c>Message</c>, <c>When</c>,
    /// <c>Unless</c>, <c>ErrorCode</c> or <c>Severity</c>. The member is resolved the way C#
    /// binds it, from the most derived type up, so an attribute's own <c>new Message</c> is not
    /// a base member and is copied into the instance like any other named argument.
    /// </summary>
    public static bool IsBaseMemberArgument(INamedTypeSymbol attrClass, string name) =>
        FindNamedArgumentMember(attrClass, name)?.ContainingType is { } declaringType
        && IsValidationAttributeBase(declaringType);

    private static bool IsValidationAttributeBase(INamedTypeSymbol type) =>
        string.Equals(type.MetadataName, ValidationAttributeMetadataName, StringComparison.Ordinal)
        && string.Equals(type.ContainingNamespace?.ToDisplayString(), ValidationNamespace, StringComparison.Ordinal);

    /// <summary>
    /// The symbol a named argument assigns: a property's setter, which can be narrower than the
    /// property itself, or a field. Searched from <paramref name="attrClass"/> up its base types.
    /// </summary>
    private static ISymbol? FindNamedArgumentMember(INamedTypeSymbol attrClass, string name)
    {
        for (INamedTypeSymbol? current = attrClass; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(name))
            {
                switch (member)
                {
                    case IPropertySymbol property:
                        return (ISymbol?)property.SetMethod ?? property;
                    case IFieldSymbol field:
                        return field;
                }
            }
        }
        return null;
    }

    /// <summary>Name of the static field holding the rule instance for one usage.</summary>
    public static string FieldName(string propName, int ruleIndex) => $"__Rule_{propName}_{ruleIndex}";

    /// <summary>
    /// Rebuilds a usage as a C# object creation, for example
    /// <c>new global::Ns.MinWordsAttribute(3) { Tag = "x" }</c>. The base members
    /// <c>Message</c>, <c>When</c>, <c>Unless</c>, <c>ErrorCode</c> and <c>Severity</c> are
    /// read at compile time by the emitter, so they are not copied into the instance. A member
    /// the attribute declares itself is copied, even one that hides a base member by name.
    /// </summary>
    public static string BuildInitializer(AttributeData attr)
    {
        var sb = new StringBuilder();
        sb.Append("new ");
        sb.Append(attr.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        sb.Append('(');
        for (int i = 0; i < attr.ConstructorArguments.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(RenderConstant(attr.ConstructorArguments[i]));
        }
        sb.Append(')');

        var first = true;
        foreach (var named in attr.NamedArguments)
        {
            if (IsBaseMemberArgument(attr.AttributeClass!, named.Key)) continue;
            sb.Append(first ? " { " : ", ");
            sb.Append(named.Key);
            sb.Append(" = ");
            sb.Append(RenderConstant(named.Value));
            first = false;
        }
        if (!first) sb.Append(" }");

        return sb.ToString();
    }

    /// <summary>
    /// The message and error code declared by the nearest <c>[RuleMessage]</c> on
    /// <paramref name="attrClass"/> or one of its base types, or <see langword="null"/> when none
    /// declares one.
    /// </summary>
    public static (string Message, string? ErrorCode)? FindRuleMessage(INamedTypeSymbol attrClass)
    {
        for (INamedTypeSymbol? current = attrClass; current is not null; current = current.BaseType)
        {
            foreach (var attr in current.GetAttributes())
            {
                if (!IsRuleMessageAttribute(attr.AttributeClass)) continue;
                if (attr.ConstructorArguments.Length != 1 || attr.ConstructorArguments[0].Value is not string message)
                    continue;

                string? errorCode = null;
                foreach (var named in attr.NamedArguments)
                {
                    if (string.Equals(named.Key, "ErrorCode", StringComparison.Ordinal) && named.Value.Value is string code)
                        errorCode = code;
                }
                return (message, errorCode);
            }
        }
        return null;
    }

    private static bool IsRuleMessageAttribute(INamedTypeSymbol? type) =>
        type is not null
        && string.Equals(type.MetadataName, RuleMessageAttributeMetadataName, StringComparison.Ordinal)
        && string.Equals(type.ContainingNamespace?.ToDisplayString(), ValidationNamespace, StringComparison.Ordinal);

    /// <summary>
    /// Resolves a custom rule's message template in one pass. <c>{PropertyName}</c> becomes
    /// <paramref name="displayName"/>, and each other <c>{name}</c> becomes the constant written on
    /// the usage for the constructor parameter of that name, or else for the named argument of that
    /// name. Matching is case-sensitive. <c>{PropertyValue}</c> is left for validation time. A name
    /// that matches nothing is kept as written and returned in <paramref name="unknown"/>, once per
    /// name. Substituted text is never read again for placeholders.
    /// </summary>
    public static MessageTemplate ResolveMessage(
        string template,
        AttributeData attr,
        string displayName,
        out IReadOnlyList<string> unknown)
    {
        List<string>? unknownNames = null;
        var resolved = MessageTemplate.Resolve(template, name =>
        {
            if (string.Equals(name, "PropertyName", StringComparison.Ordinal))
                return displayName;
            if (TryFindArgument(attr, name, out var value))
                return FormatPlaceholderValue(value);

            unknownNames ??= new List<string>();
            if (!unknownNames.Exists(n => string.Equals(n, name, StringComparison.Ordinal)))
                unknownNames.Add(name);
            return null;
        });

        unknown = unknownNames ?? (IReadOnlyList<string>)Array.Empty<string>();
        return resolved;
    }

    private static bool TryFindArgument(AttributeData attr, string name, out TypedConstant value)
    {
        var ctor = attr.AttributeConstructor;
        if (ctor is not null)
        {
            var count = Math.Min(ctor.Parameters.Length, attr.ConstructorArguments.Length);
            for (int i = 0; i < count; i++)
            {
                if (string.Equals(ctor.Parameters[i].Name, name, StringComparison.Ordinal))
                {
                    value = attr.ConstructorArguments[i];
                    return true;
                }
            }
        }

        foreach (var named in attr.NamedArguments)
        {
            if (string.Equals(named.Key, name, StringComparison.Ordinal))
            {
                value = named.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    /// <summary>
    /// Message text for a constant: invariant-culture, strings without quotes, enums by member
    /// name when one matches, <c>typeof</c> by the type's display name, arrays joined by
    /// <c>", "</c>, and <see langword="null"/> as empty text.
    /// </summary>
    private static string FormatPlaceholderValue(TypedConstant constant)
    {
        if (constant.IsNull) return "";

        switch (constant.Kind)
        {
            case TypedConstantKind.Array:
            {
                var parts = new string[constant.Values.Length];
                for (int i = 0; i < parts.Length; i++)
                    parts[i] = FormatPlaceholderValue(constant.Values[i]);
                return string.Join(", ", parts);
            }
            case TypedConstantKind.Type:
                return ((ITypeSymbol)constant.Value!).ToDisplayString();
            case TypedConstantKind.Enum:
                return FindEnumMemberName(constant) ?? FormatPrimitive(constant.Value!);
            default:
                return FormatPrimitive(constant.Value!);
        }
    }

    private static string? FindEnumMemberName(TypedConstant constant)
    {
        foreach (var member in constant.Type!.GetMembers())
        {
            if (member is IFieldSymbol { HasConstantValue: true } field && Equals(field.ConstantValue, constant.Value))
                return field.Name;
        }
        return null;
    }

    private static string FormatPrimitive(object value) =>
        value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";

    private static string RenderConstant(TypedConstant constant)
    {
        if (constant.IsNull) return "null";

        switch (constant.Kind)
        {
            case TypedConstantKind.Array:
            {
                var elementType = ((IArrayTypeSymbol)constant.Type!).ElementType
                    .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var sb = new StringBuilder();
                sb.Append("new ").Append(elementType).Append("[] {");
                for (int i = 0; i < constant.Values.Length; i++)
                {
                    sb.Append(i == 0 ? " " : ", ");
                    sb.Append(RenderConstant(constant.Values[i]));
                }
                sb.Append(constant.Values.Length == 0 ? "}" : " }");
                return sb.ToString();
            }
            case TypedConstantKind.Type:
                return $"typeof({RenderTypeOfOperand((ITypeSymbol)constant.Value!)})";
            case TypedConstantKind.Enum:
            {
                var literal = RenderPrimitive(constant.Value!);
                if (literal.StartsWith("-", StringComparison.Ordinal)) literal = $"({literal})";
                return $"({constant.Type!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){literal}";
            }
            default:
                return RenderPrimitive(constant.Value!);
        }
    }

    /// <summary>
    /// The operand of a <c>typeof</c>. An unbound generic such as <c>List&lt;&gt;</c> keeps its
    /// empty type-argument list, which the fully qualified format would otherwise fill in.
    /// </summary>
    private static string RenderTypeOfOperand(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsUnboundGenericType: true } unbound)
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        var containing = unbound.ContainingType is null
            ? unbound.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? "global::" + ns.ToDisplayString() + "."
                : "global::"
            : RenderTypeOfOperand(unbound.ContainingType) + ".";
        return $"{containing}{unbound.Name}<{new string(',', unbound.Arity - 1)}>";
    }

    /// <summary>
    /// A C# literal for a constant, with the suffix its type needs, so the literal converts to
    /// the parameter it was written for: <c>1.5F</c> binds a <c>float</c>, <c>1.5</c> would not.
    /// </summary>
    private static string RenderPrimitive(object value) => value switch
    {
        string s  => SymbolDisplay.FormatLiteral(s, quote: true),
        char c    => SymbolDisplay.FormatLiteral(c, quote: true),
        bool b    => b ? "true" : "false",
        float f   => RenderFloat(f),
        double d  => RenderDouble(d),
        decimal m => m.ToString(CultureInfo.InvariantCulture) + "M",
        long l    => l.ToString(CultureInfo.InvariantCulture) + "L",
        ulong ul  => ul.ToString(CultureInfo.InvariantCulture) + "UL",
        uint u    => u.ToString(CultureInfo.InvariantCulture) + "U",
        int i     => i.ToString(CultureInfo.InvariantCulture),
        short sh  => sh.ToString(CultureInfo.InvariantCulture),
        ushort us => us.ToString(CultureInfo.InvariantCulture),
        byte by   => by.ToString(CultureInfo.InvariantCulture),
        sbyte sb  => sb.ToString(CultureInfo.InvariantCulture),
        // Unreachable: C# only allows the primitive types above, string and char as attribute constants.
        _         => throw new InvalidOperationException($"Unsupported attribute constant of type {value.GetType()}."),
    };

    private static string RenderFloat(float f)
    {
        if (float.IsNaN(f)) return "float.NaN";
        if (float.IsPositiveInfinity(f)) return "float.PositiveInfinity";
        if (float.IsNegativeInfinity(f)) return "float.NegativeInfinity";
        return f.ToString("R", CultureInfo.InvariantCulture) + "F";
    }

    private static string RenderDouble(double d)
    {
        if (double.IsNaN(d)) return "double.NaN";
        if (double.IsPositiveInfinity(d)) return "double.PositiveInfinity";
        if (double.IsNegativeInfinity(d)) return "double.NegativeInfinity";
        return d.ToString("R", CultureInfo.InvariantCulture) + "D";
    }
}
