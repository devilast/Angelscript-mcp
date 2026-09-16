using System.Text.RegularExpressions;

namespace AngelscriptApiMcp.Api;

/// <summary>
/// Parses one <c>.hpp</c> file written by the Angelscript plugin's <c>-dump-as-doc</c> switch
/// (<c>FAngelscriptDocs::DumpDocumentation</c> in <c>AngelscriptDocs.cpp</c>).
/// </summary>
/// <remarks>
/// The files are Natural Docs input rather than compilable C++. Every entry is a doc comment followed
/// by a single declaration line:
/// <code>
/// /* Class: AActor \n doc */ \n class AActor : public UObject\n{\npublic:
/// \n// Group: Category\n
/// \n/* Variable: Name \n doc */\n[static ]Declaration;
/// \n/* Function: Name \n doc */\n[static ]Declaration {}
/// \n}\n
/// /* Enum: EName \n values */ \n enum EName { \nA,\nB, \n}
/// </code>
/// Doc text is raw Unreal tooltip text and can contain anything, so a comment ends at the first
/// <c>*/</c> that is followed by a well-formed declaration, and comment bodies are never scanned for
/// entries.
/// </remarks>
public static partial class DumpParser
{
    private const string SuperClassSeparator = " : public ";

    [GeneratedRegex(@"^// Group: (?<category>[^\n]*)$|/\* (?<kind>Class|Variable|Function|Enum): (?<name>\S+) ?\n", RegexOptions.Multiline)]
    private static partial Regex EntryRegex();

    [GeneratedRegex(@"\G ?\n class (?<decl>[^\n]*)")]
    private static partial Regex ClassDeclarationRegex();

    [GeneratedRegex(@"\G ?\n enum \S+ \{(?<values>.*?)\n\}", RegexOptions.Singleline)]
    private static partial Regex EnumDeclarationRegex();

    [GeneratedRegex(@"\G ?\n(?<decl>[^\n]*)")]
    private static partial Regex MemberDeclarationRegex();

    /// <param name="text">File contents.</param>
    /// <param name="fallbackName">Type name to use if the file has no class header (the file name).</param>
    public static ApiType Parse(string text, string fallbackName)
    {
        text = text.Replace("\r\n", "\n");

        string name = fallbackName;
        string superClass = "";
        string typeDocumentation = "";
        string category = "";
        var members = new List<ApiMember>();
        var enumValues = new List<string>();

        int position = 0;
        while (position < text.Length)
        {
            Match entry = EntryRegex().Match(text, position);
            if (!entry.Success)
                break;

            if (entry.Groups["category"].Success)
            {
                category = entry.Groups["category"].Value.Trim();
                position = entry.Index + entry.Length;
                continue;
            }

            string kind = entry.Groups["kind"].Value;
            int documentationStart = entry.Index + entry.Length;
            if (!TryReadEntry(text, documentationStart, kind, out string documentation, out Match declaration))
            {
                position = documentationStart;
                continue;
            }

            position = declaration.Index + declaration.Length;
            switch (kind)
            {
                case "Class":
                    ParseClassDeclaration(declaration.Groups["decl"].Value, ref name, ref superClass);
                    typeDocumentation = documentation;
                    break;
                case "Enum":
                    // The enum's doc comment only repeats the values ("A - Enum"), so it is not kept.
                    enumValues.AddRange(ParseEnumValues(declaration.Groups["values"].Value));
                    break;
                default:
                    members.Add(CreateMember(kind, entry.Groups["name"].Value, documentation, declaration.Groups["decl"].Value, category));
                    break;
            }
        }

        return new ApiType(name, superClass, typeDocumentation, members, enumValues);
    }

    private static bool TryReadEntry(string text, int documentationStart, string kind, out string documentation, out Match declaration)
    {
        Regex declarationRegex = kind switch
        {
            "Class" => ClassDeclarationRegex(),
            "Enum" => EnumDeclarationRegex(),
            _ => MemberDeclarationRegex(),
        };

        int searchFrom = documentationStart;
        while (true)
        {
            int close = text.IndexOf("*/", searchFrom, StringComparison.Ordinal);
            if (close < 0)
                break;

            Match candidate = declarationRegex.Match(text, close + 2);
            if (candidate.Success && IsDeclarationOf(kind, candidate))
            {
                documentation = CleanDocumentation(text[documentationStart..close]);
                declaration = candidate;
                return true;
            }

            searchFrom = close + 2;
        }

        documentation = "";
        declaration = Match.Empty;
        return false;
    }

    private static bool IsDeclarationOf(string kind, Match declaration) => kind switch
    {
        "Function" => declaration.Groups["decl"].Value.TrimEnd().EndsWith("{}", StringComparison.Ordinal),
        "Variable" => declaration.Groups["decl"].Value.TrimEnd().EndsWith(';'),
        _ => true,
    };

    private static string CleanDocumentation(string raw)
    {
        List<string> lines = raw.Split('\n').Select(line => line.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        // The writer pads the text with one space after the header line.
        if (lines.Count > 0 && lines[0].StartsWith(' '))
            lines[0] = lines[0][1..];

        return string.Join('\n', lines);
    }

    private static void ParseClassDeclaration(string declaration, ref string name, ref string superClass)
    {
        int separator = declaration.IndexOf(SuperClassSeparator, StringComparison.Ordinal);
        string declaredName = (separator >= 0 ? declaration[..separator] : declaration).Trim();
        if (declaredName.Length > 0)
            name = declaredName;
        if (separator >= 0)
            superClass = declaration[(separator + SuperClassSeparator.Length)..].Trim();
    }

    private static IEnumerable<string> ParseEnumValues(string values) =>
        values.Split('\n')
            .Select(value => value.Trim().TrimEnd(',').Trim())
            .Where(value => value.Length > 0);

    private static ApiMember CreateMember(string kind, string name, string documentation, string rawDeclaration, string category)
    {
        string declaration = rawDeclaration.Trim();
        bool isStatic = declaration.StartsWith("static ", StringComparison.Ordinal);
        if (isStatic)
            declaration = declaration["static ".Length..];

        bool isFunction = kind == "Function";
        declaration = (isFunction ? declaration[..^2] : declaration[..^1]).TrimEnd();

        if (category.Length == 0)
            category = (isStatic ? "Static " : "") + (isFunction ? "Functions" : "Variables");

        return new ApiMember(name, isFunction ? ApiMemberKind.Function : ApiMemberKind.Property, declaration, documentation, category, isStatic);
    }
}
