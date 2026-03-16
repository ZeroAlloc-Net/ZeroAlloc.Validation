using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ZValidation.Generator;

internal static class RuleEmitter
{
    private const string ValidateAttributeFqn = "ZValidation.ValidateAttribute";
    private const string ValidateWithAttributeFqn = "ZValidation.ValidateWithAttribute";

    private const string NotNullFqn               = "ZValidation.NotNullAttribute";
    private const string NotEmptyFqn              = "ZValidation.NotEmptyAttribute";
    private const string MinLengthFqn             = "ZValidation.MinLengthAttribute";
    private const string MaxLengthFqn             = "ZValidation.MaxLengthAttribute";
    private const string GreaterThanFqn           = "ZValidation.GreaterThanAttribute";
    private const string LessThanFqn              = "ZValidation.LessThanAttribute";
    private const string InclusiveBetweenFqn      = "ZValidation.InclusiveBetweenAttribute";
    private const string GreaterThanOrEqualToFqn  = "ZValidation.GreaterThanOrEqualToAttribute";
    private const string LessThanOrEqualToFqn     = "ZValidation.LessThanOrEqualToAttribute";
    private const string ExclusiveBetweenFqn      = "ZValidation.ExclusiveBetweenAttribute";
    private const string LengthFqn                = "ZValidation.LengthAttribute";
    private const string EmailAddressFqn          = "ZValidation.EmailAddressAttribute";
    private const string MatchesFqn               = "ZValidation.MatchesAttribute";
    private const string NullFqn                  = "ZValidation.NullAttribute";
    private const string EmptyFqn                 = "ZValidation.EmptyAttribute";
    private const string EqualFqn                 = "ZValidation.EqualAttribute";
    private const string NotEqualFqn              = "ZValidation.NotEqualAttribute";
    private const string IsInEnumFqn              = "ZValidation.IsInEnumAttribute";
    private const string IsEnumNameFqn            = "ZValidation.IsEnumNameAttribute";
    private const string PrecisionScaleFqn        = "ZValidation.PrecisionScaleAttribute";
    private const string MustFqn                  = "ZValidation.MustAttribute";

    private static bool IsRuleAttribute(AttributeData attr)
    {
        var fqn = attr.AttributeClass?.ToDisplayString();
        return fqn is NotNullFqn or NotEmptyFqn or MinLengthFqn or MaxLengthFqn
            or GreaterThanFqn or LessThanFqn or InclusiveBetweenFqn
            or GreaterThanOrEqualToFqn or LessThanOrEqualToFqn
            or ExclusiveBetweenFqn or LengthFqn
            or EmailAddressFqn or MatchesFqn
            or NullFqn or EmptyFqn
            or EqualFqn or NotEqualFqn
            or IsInEnumFqn
            or IsEnumNameFqn
            or PrecisionScaleFqn
            or MustFqn;
    }

    public static void EmitValidateBody(StringBuilder sb, INamedTypeSymbol classSymbol, string modelParamName = "instance")
    {
        var byProperty = CollectPropertyRules(classSymbol);
        var nestedProperties = GetNestedValidateProperties(classSymbol).ToList();
        var collectionProperties = GetCollectionValidateProperties(classSymbol).ToList();
        bool hasNested = nestedProperties.Count > 0 || collectionProperties.Count > 0;
        int totalDirectRules = byProperty.Sum(x => x.Rules.Count);

        if (hasNested)
            EmitNestedPath(sb, byProperty, nestedProperties, collectionProperties, modelParamName);
        else
            EmitFlatPath(sb, byProperty, totalDirectRules, modelParamName);
    }

    private static List<(IPropertySymbol Property, List<AttributeData> Rules)> CollectPropertyRules(INamedTypeSymbol classSymbol)
    {
        var byProperty = new List<(IPropertySymbol Property, List<AttributeData> Rules)>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            var propRules = new List<AttributeData>();
            foreach (var attr in prop.GetAttributes())
            {
                if (IsRuleAttribute(attr))
                    propRules.Add(attr);
            }
            if (propRules.Count > 0)
                byProperty.Add((prop, propRules));
        }
        return byProperty;
    }

    private static void EmitNestedPath(
        StringBuilder sb,
        List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
        List<IPropertySymbol> nestedProperties,
        List<(IPropertySymbol Property, INamedTypeSymbol ElementType)> collectionProperties,
        string modelParamName)
    {
        sb.AppendLine("        var failures = new System.Collections.Generic.List<global::ZValidation.ValidationFailure>();");
        sb.AppendLine();

        EmitPropertyRulesWithAdd(sb, byProperty, modelParamName);
        EmitNestedValidators(sb, nestedProperties, modelParamName);
        EmitCollectionValidators(sb, collectionProperties, modelParamName);

        sb.AppendLine("        return new global::ZValidation.ValidationResult(failures.ToArray());");
    }

    private static void EmitPropertyRulesWithAdd(
        StringBuilder sb,
        List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
        string modelParamName)
    {
        for (int pi = 0; pi < byProperty.Count; pi++)
        {
            var (prop, rules) = byProperty[pi];
            var propName = prop.Name;
            var propAccess = $"{modelParamName}.{propName}";

            for (int i = 0; i < rules.Count; i++)
            {
                var attr = rules[i];
                var fqn = attr.AttributeClass!.ToDisplayString();
                var prefix = i == 0 ? "        if" : "        else if";
                var message = ResolveMessage(attr, fqn, propName) ?? GetDefaultMessage(fqn, attr, propName);
                var propTypeFullName = GetNullableUnwrappedFullTypeName(prop);
                var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName, modelParamName);
                var whenMethod   = GetWhen(attr);
                var unlessMethod = GetUnless(attr);
                var whenGuard    = whenMethod   is null ? "" : $"{modelParamName}.{whenMethod}() && ";
                var unlessGuard  = unlessMethod is null ? "" : $"!{modelParamName}.{unlessMethod}() && ";

                sb.AppendLine($"{prefix} ({whenGuard}{unlessGuard}{condition})");
                sb.AppendLine($"            failures.Add({BuildFailureInitializer(propName, message, attr)});");
            }
            sb.AppendLine();
        }
    }

    private static void EmitNestedValidators(
        StringBuilder sb,
        List<IPropertySymbol> nestedProperties,
        string modelParamName)
    {
        for (int ni = 0; ni < nestedProperties.Count; ni++)
        {
            var nestedProp = nestedProperties[ni];
            var propName = nestedProp.Name;
            var camelN = char.ToLowerInvariant(propName[0]).ToString(CultureInfo.InvariantCulture) + propName.Substring(1);

            sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var nestedResult = _{camelN}Validator.Validate({modelParamName}.{propName});");
            sb.AppendLine("            foreach (ref readonly var f in nestedResult.Failures)");
            sb.AppendLine($"                failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}.\" + f.PropertyName, ErrorMessage = f.ErrorMessage }});");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }

    private static void EmitCollectionValidators(
        StringBuilder sb,
        List<(IPropertySymbol Property, INamedTypeSymbol ElementType)> collectionProperties,
        string modelParamName)
    {
        for (int ci = 0; ci < collectionProperties.Count; ci++)
        {
            var (collProp, _) = collectionProperties[ci];
            var propName = collProp.Name;
            var varName = $"_c{ci.ToString(CultureInfo.InvariantCulture)}";
            var camelC = char.ToLowerInvariant(propName[0]).ToString(CultureInfo.InvariantCulture) + propName.Substring(1);

            sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            int {varName}Idx = 0;");
            sb.AppendLine($"            foreach (var {varName}Item in {modelParamName}.{propName})");
            sb.AppendLine("            {");
            sb.AppendLine($"                if ({varName}Item is not null)");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var {varName}Result = _{camelC}Validator.Validate({varName}Item);");
            sb.AppendLine($"                    foreach (ref readonly var f in {varName}Result.Failures)");
            sb.AppendLine($"                        failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}[\" + {varName}Idx + \"].\" + f.PropertyName, ErrorMessage = f.ErrorMessage }});");
            sb.AppendLine("                }");
            sb.AppendLine($"                {varName}Idx++;");
            sb.AppendLine("            }");
            sb.AppendLine("        }");
            sb.AppendLine();
        }
    }

    private static void EmitFlatPath(
        StringBuilder sb,
        List<(IPropertySymbol Property, List<AttributeData> Rules)> byProperty,
        int totalDirectRules,
        string modelParamName)
    {
        sb.AppendLine($"        var buffer = new global::ZValidation.ValidationFailure[{totalDirectRules}];");
        sb.AppendLine("        int count = 0;");
        sb.AppendLine();

        for (int pi = 0; pi < byProperty.Count; pi++)
        {
            var (prop, rules) = byProperty[pi];
            var propName = prop.Name;
            var propAccess = $"{modelParamName}.{propName}";

            for (int i = 0; i < rules.Count; i++)
            {
                var attr = rules[i];
                var fqn = attr.AttributeClass!.ToDisplayString();
                var prefix = i == 0 ? "        if" : "        else if";
                var message = ResolveMessage(attr, fqn, propName) ?? GetDefaultMessage(fqn, attr, propName);
                var propTypeFullName = GetNullableUnwrappedFullTypeName(prop);
                var condition = BuildCondition(fqn, attr, propAccess, propTypeFullName, modelParamName);
                var whenMethod   = GetWhen(attr);
                var unlessMethod = GetUnless(attr);
                var whenGuard    = whenMethod   is null ? "" : $"{modelParamName}.{whenMethod}() && ";
                var unlessGuard  = unlessMethod is null ? "" : $"!{modelParamName}.{unlessMethod}() && ";

                sb.AppendLine($"{prefix} ({whenGuard}{unlessGuard}{condition})");
                sb.AppendLine($"            buffer[count++] = {BuildFailureInitializer(propName, message, attr)};");
            }
            sb.AppendLine();
        }

        sb.AppendLine("        if (count == buffer.Length) return new global::ZValidation.ValidationResult(buffer);");
        sb.AppendLine("        var result = new global::ZValidation.ValidationFailure[count];");
        sb.AppendLine("        global::System.Array.Copy(buffer, result, count);");
        sb.AppendLine("        return new global::ZValidation.ValidationResult(result);");
    }

    private static bool IsGlobalOrEmpty(string? namespaceName) =>
        string.IsNullOrEmpty(namespaceName)
        || string.Equals(namespaceName, "<global namespace>", StringComparison.Ordinal);

    private static string? ResolveMessage(AttributeData attr, string fqn, string propName)
    {
        var raw = GetMessage(attr);
        if (raw is null) return null;

        var result = raw.Replace("{PropertyName}", propName);

        // {ComparisonValue} — single numeric arg used by comparison validators
        if (fqn is GreaterThanFqn or LessThanFqn or GreaterThanOrEqualToFqn or LessThanOrEqualToFqn
                 or EqualFqn or NotEqualFqn)
        {
            var val = fqn is (EqualFqn or NotEqualFqn) && IsStringArg(attr, 0)
                ? GetStringArg(attr, 0)
                : GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture);
            result = result.Replace("{ComparisonValue}", val);
        }

        // {MinLength} / {MaxLength}
        if (fqn is LengthFqn)
        {
            result = result
                .Replace("{MinLength}", GetIntArg(attr, 0).ToString(CultureInfo.InvariantCulture))
                .Replace("{MaxLength}", GetIntArg(attr, 1).ToString(CultureInfo.InvariantCulture));
        }
        if (fqn is MinLengthFqn)
            result = result.Replace("{MinLength}", GetIntArg(attr, 0).ToString(CultureInfo.InvariantCulture));
        if (fqn is MaxLengthFqn)
            result = result.Replace("{MaxLength}", GetIntArg(attr, 0).ToString(CultureInfo.InvariantCulture));

        // {From} / {To}
        if (fqn is ExclusiveBetweenFqn or InclusiveBetweenFqn)
        {
            result = result
                .Replace("{From}", GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture))
                .Replace("{To}",   GetDoubleArg(attr, 1).ToString(CultureInfo.InvariantCulture));
        }

        return result;
    }

    private static string? GetMessage(AttributeData attr)
    {
        foreach (var named in attr.NamedArguments)
            if (string.Equals(named.Key, "Message", StringComparison.Ordinal) && named.Value.Value is string s)
                return s;
        return null;
    }

    private static string? GetWhen(AttributeData attr)
    {
        foreach (var named in attr.NamedArguments)
            if (string.Equals(named.Key, "When", StringComparison.Ordinal) && named.Value.Value is string s)
                return s;
        return null;
    }

    private static string? GetUnless(AttributeData attr)
    {
        foreach (var named in attr.NamedArguments)
            if (string.Equals(named.Key, "Unless", StringComparison.Ordinal) && named.Value.Value is string s)
                return s;
        return null;
    }

    private static string? GetErrorCode(AttributeData attr)
    {
        foreach (var named in attr.NamedArguments)
            if (string.Equals(named.Key, "ErrorCode", StringComparison.Ordinal) && named.Value.Value is string s)
                return s;
        return null;
    }

    // Returns 0 = Error (default), 1 = Warning, 2 = Info.
    private static int GetSeverityValue(AttributeData attr)
    {
        foreach (var named in attr.NamedArguments)
            if (string.Equals(named.Key, "Severity", StringComparison.Ordinal) && named.Value.Value is int i)
                return i;
        return 0;
    }

    private static string SeverityToLiteral(int severityValue) => severityValue switch
    {
        1 => "global::ZValidation.Severity.Warning",
        2 => "global::ZValidation.Severity.Info",
        _ => "global::ZValidation.Severity.Error"
    };

    private static string BuildFailureInitializer(string propName, string message, AttributeData attr)
    {
        var errorCode = GetErrorCode(attr);
        var severityValue = GetSeverityValue(attr);

        var sb = new StringBuilder();
        sb.Append($"new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = \"{EscapeString(message)}\"");
        if (errorCode is not null)
            sb.Append($", ErrorCode = \"{EscapeString(errorCode)}\"");
        if (severityValue != 0)
            sb.Append($", Severity = {SeverityToLiteral(severityValue)}");
        sb.Append(" }");
        return sb.ToString();
    }

    private static object? GetArg(AttributeData attr, int index)
    {
        if (attr.ConstructorArguments.Length <= index) return null;
        return attr.ConstructorArguments[index].Value;
    }

    private static int GetIntArg(AttributeData attr, int index)
        => System.Convert.ToInt32(GetArg(attr, index), CultureInfo.InvariantCulture);

    private static double GetDoubleArg(AttributeData attr, int index)
        => System.Convert.ToDouble(GetArg(attr, index), CultureInfo.InvariantCulture);

    private static string GetStringArg(AttributeData attr, int index)
        => GetArg(attr, index) as string ?? string.Empty;

    private static string GetTypeArgFullName(AttributeData attr, int index)
    {
        if (attr.ConstructorArguments.Length <= index) return "global::System.Enum";
        var typeSymbol = attr.ConstructorArguments[index].Value as ITypeSymbol;
        return typeSymbol?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "global::System.Enum";
    }

    private static bool IsStringArg(AttributeData attr, int index)
    {
        if (attr.ConstructorArguments.Length <= index) return false;
        return attr.ConstructorArguments[index].Type?.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_String;
    }

    private static string BuildCondition(string fqn, AttributeData attr, string access, string propTypeFullName = "", string modelParamName = "instance") =>
        fqn switch
        {
            NotNullFqn               => $"{access} is null",
            NotEmptyFqn              => $"string.IsNullOrEmpty({access})",
            MinLengthFqn             => $"{access}.Length < {GetIntArg(attr, 0)}",
            MaxLengthFqn             => $"{access}.Length > {GetIntArg(attr, 0)}",
            GreaterThanFqn           => $"System.Convert.ToDouble({access}) <= {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
            LessThanFqn              => $"System.Convert.ToDouble({access}) >= {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
            InclusiveBetweenFqn      => $"System.Convert.ToDouble({access}) < {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)} || System.Convert.ToDouble({access}) > {GetDoubleArg(attr, 1).ToString(CultureInfo.InvariantCulture)}",
            GreaterThanOrEqualToFqn  => $"System.Convert.ToDouble({access}) < {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
            LessThanOrEqualToFqn     => $"System.Convert.ToDouble({access}) > {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
            ExclusiveBetweenFqn      => $"System.Convert.ToDouble({access}) <= {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)} || System.Convert.ToDouble({access}) >= {GetDoubleArg(attr, 1).ToString(CultureInfo.InvariantCulture)}",
            LengthFqn                => $"{access}.Length < {GetIntArg(attr, 0)} || {access}.Length > {GetIntArg(attr, 1)}",
            EmailAddressFqn          => $"!global::ZValidationInternal.EmailValidator.IsValid({access})",
            MatchesFqn               => $"!global::System.Text.RegularExpressions.Regex.IsMatch({access} ?? \"\", \"{EscapeString(GetStringArg(attr, 0))}\")",
            NullFqn                  => $"{access} is not null",
            EmptyFqn                 => $"!string.IsNullOrEmpty({access})",
            EqualFqn                 => IsStringArg(attr, 0)
                ? $"{access} != \"{EscapeString(GetStringArg(attr, 0))}\""
                : $"System.Convert.ToDouble({access}) != {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
            NotEqualFqn              => IsStringArg(attr, 0)
                ? $"{access} == \"{EscapeString(GetStringArg(attr, 0))}\""
                : $"System.Convert.ToDouble({access}) == {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
            IsInEnumFqn              => $"!global::System.Enum.IsDefined(typeof({propTypeFullName}), {access})",
            IsEnumNameFqn            => $"!global::System.Enum.IsDefined(typeof({GetTypeArgFullName(attr, 0)}), {access})",
            PrecisionScaleFqn        => $"global::ZValidationInternal.DecimalValidator.ExceedsPrecisionScale({access}, {GetIntArg(attr, 0)}, {GetIntArg(attr, 1)})",
            MustFqn                  => $"!{modelParamName}.{GetStringArg(attr, 0)}({access})",
            _                        => "false"
        };

    private static string GetDefaultMessage(string fqn, AttributeData attr, string propName) =>
        fqn switch
        {
            NotNullFqn               => $"{propName} must not be null.",
            NotEmptyFqn              => $"{propName} must not be empty.",
            MinLengthFqn             => $"{propName} must be at least {GetArg(attr, 0)} characters.",
            MaxLengthFqn             => $"{propName} must not exceed {GetArg(attr, 0)} characters.",
            GreaterThanFqn           => $"{propName} must be greater than {GetArg(attr, 0)}.",
            LessThanFqn              => $"{propName} must be less than {GetArg(attr, 0)}.",
            InclusiveBetweenFqn      => $"{propName} must be between {GetArg(attr, 0)} and {GetArg(attr, 1)}.",
            GreaterThanOrEqualToFqn  => $"{propName} must be greater than or equal to {GetArg(attr, 0)}.",
            LessThanOrEqualToFqn     => $"{propName} must be less than or equal to {GetArg(attr, 0)}.",
            ExclusiveBetweenFqn      => $"{propName} must be exclusively between {GetArg(attr, 0)} and {GetArg(attr, 1)}.",
            LengthFqn                => $"{propName} must be between {GetArg(attr, 0)} and {GetArg(attr, 1)} characters.",
            EmailAddressFqn          => $"{propName} must be a valid email address.",
            MatchesFqn               => $"{propName} does not match the required pattern.",
            NullFqn                  => $"{propName} must be null.",
            EmptyFqn                 => $"{propName} must be empty.",
            EqualFqn                 => IsStringArg(attr, 0)
                ? $"{propName} must equal \"{GetStringArg(attr, 0)}\"."
                : $"{propName} must equal {GetArg(attr, 0)}.",
            NotEqualFqn              => IsStringArg(attr, 0)
                ? $"{propName} must not equal \"{GetStringArg(attr, 0)}\"."
                : $"{propName} must not equal {GetArg(attr, 0)}.",
            IsInEnumFqn              => $"{propName} is not a valid value.",
            IsEnumNameFqn            => $"{propName} is not a valid enum name.",
            PrecisionScaleFqn        => $"{propName} must not exceed {GetArg(attr, 0)} digits total with {GetArg(attr, 1)} decimal places.",
            MustFqn                  => $"{propName} is invalid.",
            _                        => $"{propName} is invalid."
        };

    private static string GetNullableUnwrappedFullTypeName(IPropertySymbol prop)
    {
        var type = prop.Type;
        if (type is INamedTypeSymbol named && named.IsGenericType
            && string.Equals(named.OriginalDefinition.ToDisplayString(), "System.Nullable<T>", StringComparison.Ordinal))
            type = named.TypeArguments[0];
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static INamedTypeSymbol? GetValidateWithType(IPropertySymbol prop)
    {
        foreach (var attr in prop.GetAttributes())
        {
            if (!string.Equals(attr.AttributeClass?.ToDisplayString(), ValidateWithAttributeFqn, StringComparison.Ordinal))
                continue;
            if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is INamedTypeSymbol t)
                return t;
        }
        return null;
    }

    private static IEnumerable<IPropertySymbol> GetNestedValidateProperties(INamedTypeSymbol classSymbol) =>
        classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            // First arm: type has [Validate] (auto-compose) — also covers the overlap where [ValidateWith] is present on a [Validate] type; [ValidateWith] wins in CollectNestedValidatorFields.
            // Second arm: [ValidateWith] on a non-collection property whose type has no [Validate].
            .Where(p =>
                (p.Type is INamedTypeSymbol t && HasValidateAttribute(t))
                || (GetValidateWithType(p) is not null && GetCollectionElementType(p) is null));

    private static bool HasValidateAttribute(INamedTypeSymbol typeSymbol) =>
        typeSymbol.GetAttributes()
            .Any(a => string.Equals(a.AttributeClass?.ToDisplayString(), ValidateAttributeFqn, StringComparison.Ordinal));

    private static ITypeSymbol? GetCollectionElementType(IPropertySymbol prop)
    {
        // T[]
        if (prop.Type is IArrayTypeSymbol arr)
            return arr.ElementType;

        if (prop.Type is not INamedTypeSymbol named)
            return null;

        // IEnumerable<T> directly
        if (named.IsGenericType && named.TypeArguments.Length == 1
            && string.Equals(named.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.IEnumerable<T>", StringComparison.Ordinal))
            return named.TypeArguments[0];

        // Any type implementing IEnumerable<T> (List<T>, IList<T>, ICollection<T>, etc.)
        foreach (var iface in named.AllInterfaces)
        {
            if (iface.IsGenericType && iface.TypeArguments.Length == 1
                && string.Equals(iface.OriginalDefinition.ToDisplayString(), "System.Collections.Generic.IEnumerable<T>", StringComparison.Ordinal))
                return iface.TypeArguments[0];
        }

        return null;
    }

    private static IEnumerable<(IPropertySymbol Property, INamedTypeSymbol ElementType)> GetCollectionValidateProperties(INamedTypeSymbol classSymbol) =>
        classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Select(p =>
            {
                var elemType = GetCollectionElementType(p) as INamedTypeSymbol;
                if (elemType is not null && HasValidateAttribute(elemType))
                    return ((IPropertySymbol, INamedTypeSymbol)?)(p, elemType);
                if (elemType is not null && GetValidateWithType(p) is not null)
                    return (p, elemType);
                return null;
            })
            .Where(x => x.HasValue)
            .Select(x => x!.Value);

    public static System.Collections.Generic.List<(string FieldName, string ParamName, string QualifiedValidatorType)>
        CollectNestedValidatorFields(INamedTypeSymbol classSymbol)
    {
        var result = new System.Collections.Generic.List<(string, string, string)>();
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;

            string? qualifiedType = null;

            // [ValidateWith] takes priority over auto-detect for both scalar and collection properties.
            // For collections, the specified type is the element validator — GetCollectionElementType is not needed here.
            var validateWithType = GetValidateWithType(prop);
            if (validateWithType is not null)
            {
                var ns2 = validateWithType.ContainingNamespace?.ToDisplayString();
                qualifiedType = IsGlobalOrEmpty(ns2)
                    ? $"global::{validateWithType.Name}"
                    : $"global::{ns2}.{validateWithType.Name}";
            }
            // Single nested type with [Validate]
            else if (prop.Type is INamedTypeSymbol nestedNamed && HasValidateAttribute(nestedNamed))
            {
                var ns = nestedNamed.ContainingNamespace?.ToDisplayString();
                qualifiedType = IsGlobalOrEmpty(ns)
                    ? $"global::{nestedNamed.Name}Validator"
                    : $"global::{ns}.{nestedNamed.Name}Validator";
            }
            // Collection element type with [Validate]
            else if (GetCollectionElementType(prop) is INamedTypeSymbol elemNamed && HasValidateAttribute(elemNamed))
            {
                var ns = elemNamed.ContainingNamespace?.ToDisplayString();
                qualifiedType = IsGlobalOrEmpty(ns)
                    ? $"global::{elemNamed.Name}Validator"
                    : $"global::{ns}.{elemNamed.Name}Validator";
            }

            if (qualifiedType is null) continue;

            var propName = prop.Name;
            var camel = char.ToLowerInvariant(propName[0]).ToString(CultureInfo.InvariantCulture)
                        + propName.Substring(1);
            result.Add(($"_{camel}Validator", $"{camel}Validator", qualifiedType));
        }
        return result;
    }

    internal static ITypeSymbol? GetCollectionElementTypePublic(IPropertySymbol prop) => GetCollectionElementType(prop);
}
