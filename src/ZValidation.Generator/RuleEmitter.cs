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

    private static bool IsRuleAttribute(AttributeData attr)
    {
        var fqn = attr.AttributeClass?.ToDisplayString();
        return fqn is NotNullFqn or NotEmptyFqn or MinLengthFqn or MaxLengthFqn
            or GreaterThanFqn or LessThanFqn or InclusiveBetweenFqn
            or EmailAddressFqn or MatchesFqn;
    }

    public static void EmitValidateBody(StringBuilder sb, INamedTypeSymbol classSymbol, string modelParamName = "instance")
    {
        var byProperty = new List<(IPropertySymbol Property, List<AttributeData> Rules)>();
        // Group rules by property in declaration order
        foreach (var member in classSymbol.GetMembers())
        {
            if (member is not IPropertySymbol prop) continue;
            var propRules = prop.GetAttributes().Where(IsRuleAttribute).ToList();
            if (propRules.Count > 0)
                byProperty.Add((prop, propRules));
        }

        var nestedProperties = GetNestedValidateProperties(classSymbol).ToList();
        var collectionProperties = GetCollectionValidateProperties(classSymbol).ToList();
        bool hasNested = nestedProperties.Count > 0 || collectionProperties.Count > 0;
        int totalDirectRules = byProperty.Sum(x => x.Rules.Count);

        if (hasNested)
        {
            // Use List<> — nested validator failure count unknown at compile time
            sb.AppendLine("        var failures = new System.Collections.Generic.List<global::ZValidation.ValidationFailure>();");
            sb.AppendLine();

            // Direct rules — use failures.Add(...)
            foreach (var (prop, rules) in byProperty)
            {
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

            // Nested validators
            foreach (var nestedProp in nestedProperties)
            {
                var propName = nestedProp.Name;
                var nestedNamespace = nestedProp.Type.ContainingNamespace?.ToDisplayString();
                var nestedTypeName = nestedProp.Type.Name;
                var validatorName = $"{nestedTypeName}Validator";
                var qualifiedValidatorName = string.IsNullOrEmpty(nestedNamespace) || nestedNamespace == "<global namespace>"
                    ? $"global::{validatorName}"
                    : $"global::{nestedNamespace}.{validatorName}";

                sb.AppendLine($"        if ({modelParamName}.{propName} is not null)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var nestedResult = new {qualifiedValidatorName}().Validate({modelParamName}.{propName});");
                sb.AppendLine("            foreach (var f in nestedResult.Failures)");
                sb.AppendLine($"                failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}.\" + f.PropertyName, ErrorMessage = f.ErrorMessage }});");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            // Collection validators
            for (int _ci = 0; _ci < collectionProperties.Count; _ci++)
            {
                var (collProp, elementType) = collectionProperties[_ci];
                var propName = collProp.Name;
                var varName = $"_c{_ci}";   // _c0, _c1, _c2, ... — guaranteed unique
                var elemNamespace = elementType.ContainingNamespace?.ToDisplayString();
                var elemTypeName = elementType.Name;
                var collValidatorName = $"{elemTypeName}Validator";
                var qualifiedCollValidatorName = string.IsNullOrEmpty(elemNamespace) || elemNamespace == "<global namespace>"
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
                sb.AppendLine($"                    foreach (var f in {varName}Result.Failures)");
                sb.AppendLine($"                        failures.Add(new global::ZValidation.ValidationFailure {{ PropertyName = \"{propName}[\" + {varName}Idx + \"].\" + f.PropertyName, ErrorMessage = f.ErrorMessage }});");
                sb.AppendLine("                }");
                sb.AppendLine($"                {varName}Idx++;");
                sb.AppendLine("            }");
                sb.AppendLine("        }");
                sb.AppendLine();
            }

            sb.AppendLine("        return new global::ZValidation.ValidationResult(failures.ToArray());");
        }
        else
        {
            // Flat model — keep existing fixed array path
            sb.AppendLine($"        var buffer = new global::ZValidation.ValidationFailure[{totalDirectRules}];");
            sb.AppendLine("        int count = 0;");
            sb.AppendLine();

            foreach (var (prop, rules) in byProperty)
            {
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
    }

    private static string? GetMessage(AttributeData attr)
    {
        foreach (var named in attr.NamedArguments)
            if (named.Key == "Message" && named.Value.Value is string s)
                return s;
        return null;
    }

    private static object? GetArg(AttributeData attr, int index)
    {
        if (attr.ConstructorArguments.Length <= index) return null;
        return attr.ConstructorArguments[index].Value;
    }

    private static int GetIntArg(AttributeData attr, int index)
        => System.Convert.ToInt32(GetArg(attr, index));

    private static double GetDoubleArg(AttributeData attr, int index)
        => System.Convert.ToDouble(GetArg(attr, index));

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
            .Any(a => a.AttributeClass?.ToDisplayString() == ValidateAttributeFqn);

    private static ITypeSymbol? GetCollectionElementType(IPropertySymbol prop)
    {
        // T[]
        if (prop.Type is IArrayTypeSymbol arr)
            return arr.ElementType;

        if (prop.Type is not INamedTypeSymbol named)
            return null;

        // IEnumerable<T> directly
        if (named.IsGenericType && named.TypeArguments.Length == 1
            && named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
            return named.TypeArguments[0];

        // Any type implementing IEnumerable<T> (List<T>, IList<T>, ICollection<T>, etc.)
        foreach (var iface in named.AllInterfaces)
        {
            if (iface.IsGenericType && iface.TypeArguments.Length == 1
                && iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
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
