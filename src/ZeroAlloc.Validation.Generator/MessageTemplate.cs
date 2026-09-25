using System.Collections.Generic;
using System.Text;

namespace ZeroAlloc.Validation.Generator;

/// <summary>
/// A rule's failure message after compile-time resolution: literal text, split at each
/// <c>{PropertyValue}</c>, which is the one placeholder formatted at validation time. The runtime
/// value goes between each pair of <see cref="Parts"/>, so a message without
/// <c>{PropertyValue}</c> has exactly one part.
/// </summary>
/// <remarks>
/// A template is read once, left to right, and each placeholder is substituted exactly once.
/// Substituted text is never scanned again, so a display name, argument or comparison value that
/// itself contains <c>{PropertyName}</c>, <c>{PropertyValue}</c> or any other token appears
/// exactly as written.
/// </remarks>
internal sealed class MessageTemplate
{
    private const string PropertyValueName = "PropertyValue";

    private MessageTemplate(IReadOnlyList<string> parts) => Parts = parts;

    /// <summary>The literal segments; a <c>{PropertyValue}</c> hole sits between each pair.</summary>
    public IReadOnlyList<string> Parts { get; }

    /// <summary>Whether the message formats the property value at validation time.</summary>
    public bool HasPropertyValue => Parts.Count > 1;

    /// <summary>A message that is literal text throughout, with no placeholders left in it.</summary>
    public static MessageTemplate Literal(string text) => new([text]);

    /// <summary>
    /// Reads <paramref name="template"/> once. Each <c>{name}</c> becomes the text
    /// <paramref name="resolve"/> returns for it, <c>{PropertyValue}</c> becomes a runtime hole,
    /// and a name <paramref name="resolve"/> returns <see langword="null"/> for is kept as written.
    /// Text outside a well-formed <c>{name}</c> is copied unchanged.
    /// </summary>
    public static MessageTemplate Resolve(string template, System.Func<string, string?> resolve)
    {
        var parts = new List<string>();
        var sb = new StringBuilder(template.Length);
        var pos = 0;
        while (pos < template.Length)
        {
            var open = template.IndexOf('{', pos);
            if (open < 0) break;
            var close = template.IndexOf('}', open + 1);
            if (close < 0) break;

            var name = template.Substring(open + 1, close - open - 1);
            if (!IsPlaceholderName(name))
            {
                // Copy up to the '{' only, so a '{' inside a non-placeholder span is scanned again.
                sb.Append(template, pos, open + 1 - pos);
                pos = open + 1;
                continue;
            }

            sb.Append(template, pos, open - pos);
            if (string.Equals(name, PropertyValueName, System.StringComparison.Ordinal))
            {
                parts.Add(sb.ToString());
                sb.Clear();
            }
            else if (resolve(name) is { } value)
            {
                sb.Append(value);
            }
            else
            {
                sb.Append(template, open, close + 1 - open);
            }
            pos = close + 1;
        }
        sb.Append(template, pos, template.Length - pos);
        parts.Add(sb.ToString());
        return new MessageTemplate(parts);
    }

    /// <summary>
    /// Whether <paramref name="name"/> can be a placeholder: an identifier of letters, digits and
    /// underscores that does not start with a digit.
    /// </summary>
    private static bool IsPlaceholderName(string name)
    {
        if (name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_')) return false;
        foreach (var c in name)
        {
            if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
        }
        return true;
    }
}
