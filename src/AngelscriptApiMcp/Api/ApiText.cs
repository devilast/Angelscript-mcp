using System.Text.RegularExpressions;

namespace AngelscriptApiMcp.Api;

public static partial class ApiText
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>The first paragraph of a doc comment on one line, shortened to <paramref name="maxLength"/>.</summary>
    public static string Summarize(string documentation, int maxLength = 160)
    {
        if (documentation.Length == 0)
            return "";

        int paragraphEnd = documentation.IndexOf("\n\n", StringComparison.Ordinal);
        string summary = Whitespace().Replace(paragraphEnd >= 0 ? documentation[..paragraphEnd] : documentation, " ").Trim();
        if (summary.Length <= maxLength)
            return summary;

        int cut = summary.LastIndexOf(' ', maxLength);
        return summary[..(cut > maxLength / 2 ? cut : maxLength)] + "…";
    }

    public static string Indent(string text, string prefix) =>
        string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? line : prefix + line));

    /// <summary>Cuts a tool result at a line break so it stays inside the client's output budget.</summary>
    public static string Truncate(string text, int maxChars, string hint)
    {
        if (text.Length <= maxChars)
            return text;

        int cut = text.LastIndexOf('\n', maxChars);
        return text[..(cut > 0 ? cut : maxChars)] + $"\n\n[Truncated at {maxChars:N0} of {text.Length:N0} characters. {hint}]";
    }
}
