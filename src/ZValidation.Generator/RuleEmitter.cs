using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ZValidation.Generator;

internal static class RuleEmitter
{
    private const string ValidateAttributeFqn = "ZValidation.ValidateAttribute";

    private const string NotNullFqn          = "ZValidation.NotNullAttribute";
    private const string NotEmptyFqn         = "ZValidation.NotEmptyAttribute";
    private const string MinLengthFqn        = "ZValidation.MinLengthAttribute";
    private const string MaxLengthFqn        = "ZValidation.MaxLengthAttribute";
    private const string GreaterThanFqn      = "ZValidation.GreaterThanAttribute";
    private const string LessThanFqn         = "ZValidation.LessThanAttribute";
    private const string InclusiveBetweenFqn = "ZValidation.InclusiveBetweenAttribute";
    private const string EmailAddressFqn     = "ZValidation.EmailAddressAttribute";
    private const string MatchesFqn          = "ZValidation.MatchesAttribute";
    private const string NullFqn             = "ZValidation.NullAttribute";
    private const string EmptyFqn            = "ZValidation.EmptyAttribute";

    private static bool IsRuleAttribute(AttributeData attr)
    {
        var fqn = attr.AttributeClass?.ToDisplayString();
        return fqn is NotNullFqn or NotEmptyFqn or MinLengthFqn or MaxLengthFqn
            or GreaterThanFqn or LessThanFqn or InclusiveBetweenFqn
            or EmailAddressFqn or MatchesFqn
            or NullFqn or EmptyFqn;
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
                var message = GetMessage(attr) ?? GetDefaultMessage(fqn, attr, propName);
                var condition = BuildCondition(fqn, attr, propAccess);

                sb.AppendLine($"{prefix} ({condition})");
                sb.AppendLine($"            failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = \"{EscapeString(message)}\" }});");
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
            var nestedNamespace = nestedProp.Type.ContainingNamespace?.ToDisplayString();
            var nestedTypeName = nestedProp.Type.Name;
            var validatorName = $"{nestedTypeName}Validator";
            var qualifiedValidatorName = IsGlobalOrEmpty(nestedNamespace)
                ? $"global::{validatorName}"
                : $"global::{nestedNamespace}.{validatorName}";

            sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            var nestedResult = new {qualifiedValidatorName}().Validate({modelParamName}.{propName});");
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
            var (collProp, elementType) = collectionProperties[ci];
            var propName = collProp.Name;
            var varName = $"_c{ci.ToString(CultureInfo.InvariantCulture)}";
            var elemNamespace = elementType.ContainingNamespace?.ToDisplayString();
            var elemTypeName = elementType.Name;
            var collValidatorName = $"{elemTypeName}Validator";
            var qualifiedCollValidatorName = IsGlobalOrEmpty(elemNamespace)
                ? $"global::{collValidatorName}"
                : $"global::{elemNamespace}.{collValidatorName}";

            sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
            sb.AppendLine("        {");
            sb.AppendLine($"            int {varName}Idx = 0;");
            sb.AppendLine($"            foreach (var {varName}Item in {modelParamName}.{propName})");
            sb.AppendLine("            {");
            sb.AppendLine($"                if ({varName}Item is not null)");
            sb.AppendLine("                {");
            sb.AppendLine($"                    var {varName}Result = new {qualifiedCollValidatorName}().Validate({varName}Item);");
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
                var message = GetMessage(attr) ?? GetDefaultMessage(fqn, attr, propName);
                var condition = BuildCondition(fqn, attr, propAccess);

                sb.AppendLine($"{prefix} ({condition})");
                sb.AppendLine($"            buffer[count++] = new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}\", ErrorMessage = \"{EscapeString(message)}\" }};");
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

    private static string? GetMessage(AttributeData attr)
    {
        foreach (var named in attr.NamedArguments)
            if (string.Equals(named.Key, "Message", StringComparison.Ordinal) && named.Value.Value is string s)
                return s;
        return null;
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

    private static string BuildCondition(string fqn, AttributeData attr, string access) =>
        fqn switch
        {
            NotNullFqn          => $"{access} is null",
            NotEmptyFqn         => $"string.IsNullOrEmpty({access})",
            MinLengthFqn        => $"{access}.Length < {GetIntArg(attr, 0)}",
            MaxLengthFqn        => $"{access}.Length > {GetIntArg(attr, 0)}",
            GreaterThanFqn      => $"System.Convert.ToDouble({access}) <= {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
            LessThanFqn         => $"System.Convert.ToDouble({access}) >= {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)}",
            InclusiveBetweenFqn => $"System.Convert.ToDouble({access}) < {GetDoubleArg(attr, 0).ToString(CultureInfo.InvariantCulture)} || System.Convert.ToDouble({access}) > {GetDoubleArg(attr, 1).ToString(CultureInfo.InvariantCulture)}",
            EmailAddressFqn     => $"!global::ZValidationInternal.EmailValidator.IsValid({access})",
            MatchesFqn          => $"!global::System.Text.RegularExpressions.Regex.IsMatch({access} ?? \"\", \"{EscapeString(GetStringArg(attr, 0))}\")",
            NullFqn             => $"{access} is not null",
            EmptyFqn            => $"!string.IsNullOrEmpty({access})",
            _                   => "false"
        };

    private static string GetDefaultMessage(string fqn, AttributeData attr, string propName) =>
        fqn switch
        {
            NotNullFqn          => $"{propName} must not be null.",
            NotEmptyFqn         => $"{propName} must not be empty.",
            MinLengthFqn        => $"{propName} must be at least {GetArg(attr, 0)} characters.",
            MaxLengthFqn        => $"{propName} must not exceed {GetArg(attr, 0)} characters.",
            GreaterThanFqn      => $"{propName} must be greater than {GetArg(attr, 0)}.",
            LessThanFqn         => $"{propName} must be less than {GetArg(attr, 0)}.",
            InclusiveBetweenFqn => $"{propName} must be between {GetArg(attr, 0)} and {GetArg(attr, 1)}.",
            EmailAddressFqn     => $"{propName} must be a valid email address.",
            MatchesFqn          => $"{propName} does not match the required pattern.",
            NullFqn             => $"{propName} must be null.",
            EmptyFqn            => $"{propName} must be empty.",
            _                   => $"{propName} is invalid."
        };

    private static string EscapeString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static IEnumerable<IPropertySymbol> GetNestedValidateProperties(INamedTypeSymbol classSymbol) =>
        classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.Type is INamedTypeSymbol t && HasValidateAttribute(t));

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
            .Select(p => (Property: p, ElementType: GetCollectionElementType(p) as INamedTypeSymbol))
            .Where(x => x.ElementType is not null && HasValidateAttribute(x.ElementType!))
            .Select(x => (x.Property, x.ElementType!));
}
