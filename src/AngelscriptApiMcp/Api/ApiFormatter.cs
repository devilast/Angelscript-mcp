using System.Text;

namespace AngelscriptApiMcp.Api;

/// <summary>Renders index lookups as compact Markdown for tool results.</summary>
public static class ApiFormatter
{
    /// <summary>Stays well below the default MCP output limit of common clients (about 25k tokens).</summary>
    public const int MaxOutputChars = 60_000;

    private const int MaxListedSubclasses = 40;
    private const int MaxListedDeclarations = 30;

    public static string FormatSearch(string query, IReadOnlyList<SearchHit> hits, int limit, ApiIndex index)
    {
        if (hits.Count == 0)
            return $"No API matches for \"{query}\". Try a shorter name fragment, or search_guide for language features." + DumpNote(index);

        var output = new StringBuilder();
        output.Append($"{hits.Count} match(es) for \"{query}\"");
        if (hits.Count >= limit)
            output.Append($" (limit {limit}; refine the query or raise the limit)");
        output.AppendLine(":");

        foreach (SearchHit hit in hits)
            output.AppendLine("- " + DescribeHit(hit));

        return output.ToString();
    }

    public static string FormatType(ApiIndex index, ApiType type, bool includeInherited, string filter, bool includeDocs)
    {
        var output = new StringBuilder();
        output.AppendLine($"# {type.Name} ({type.KindLabel})");
        if (type.Source is { } source)
        {
            output.AppendLine($"Declared in {source}");
            if (type.Declaration.Length > 0)
                output.AppendLine($"`{type.Declaration}`");
        }

        IReadOnlyList<ApiType> ancestors = index.GetAncestors(type);
        if (type.SuperClass.Length > 0)
        {
            var chain = new List<string> { type.Name };
            chain.AddRange(ancestors.Select(ancestor => ancestor.IsScript ? ancestor.Name + " (script)" : ancestor.Name));
            string missingParent = ancestors.Count > 0 ? ancestors[^1].SuperClass : type.SuperClass;
            if (missingParent.Length > 0)
                chain.Add(missingParent + " (not indexed)");
            output.AppendLine("Inheritance: " + string.Join(" → ", chain));
        }

        if (type.Documentation.Length > 0)
            output.AppendLine().AppendLine(type.Documentation);

        IReadOnlyList<ApiType> subclasses = index.GetSubclasses(type);
        if (subclasses.Count > 0)
        {
            output.AppendLine().Append($"Direct subclasses ({subclasses.Count}): ")
                .Append(string.Join(", ", subclasses.Take(MaxListedSubclasses).Select(subclass => subclass.IsScript ? subclass.Name + " (script)" : subclass.Name)));
            if (subclasses.Count > MaxListedSubclasses)
                output.Append($", … (+{subclasses.Count - MaxListedSubclasses} more)");
            output.AppendLine();
        }

        if (type.EnumValues.Count > 0)
        {
            output.AppendLine().AppendLine("## Values");
            foreach (string value in type.EnumValues)
                output.AppendLine("- " + value);
        }

        int shown = AppendMembers(output, type, filter, includeDocs, heading: null);
        if (includeInherited)
        {
            foreach (ApiType ancestor in ancestors)
                shown += AppendMembers(output, ancestor, filter, includeDocs, heading: $"Inherited from {ancestor.Name}");
        }
        else if (ancestors.Count > 0)
        {
            output.AppendLine().AppendLine($"(Members inherited from {string.Join(", ", ancestors.Select(a => a.Name))} are not shown; pass includeInherited=true to include them.)");
        }

        if (shown == 0 && filter.Length > 0)
            output.AppendLine().AppendLine($"No members contain \"{filter}\".");

        return ApiText.Truncate(output.ToString(), MaxOutputChars, "Pass a filter to narrow the member list.");
    }

    public static string FormatMember(ApiIndex index, string typeName, string memberName)
    {
        memberName = memberName.Trim();
        if (typeName.Trim().Length == 0)
        {
            IReadOnlyList<ApiMember> everywhere = index.FindMembers(memberName);
            if (everywhere.Count == 0)
                return $"No function or property is named \"{memberName}\". " + SimilarMembers(index, memberName) + DumpNote(index);

            int typeCount = everywhere.Select(member => member.DeclaringType).Distinct().Count();
            return RenderDeclarations($"# {memberName} ({everywhere.Count} declaration(s) on {typeCount} type(s))", everywhere);
        }

        ApiType? type = index.FindType(typeName);
        if (type is null)
            return TypeNotFound(index, typeName);

        IReadOnlyList<ApiType> searched = [type, .. index.GetAncestors(type)];
        List<ApiMember> matches = searched
            .SelectMany(owner => owner.Members)
            .Where(member => member.Name.Equals(memberName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matches.Count > 0)
            return RenderDeclarations($"# {matches[0].QualifiedName} ({matches.Count} declaration(s))", matches);

        var message = new StringBuilder();
        message.AppendLine($"{type.Name} has no member named \"{memberName}\" (searched {string.Join(", ", searched.Select(owner => owner.Name))}).");

        List<string> similar = searched
            .SelectMany(owner => owner.Members)
            .Select(member => (Member: member, Score: ApiIndex.NameScore(member.Name, memberName, isTypeName: false)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .Select(candidate => candidate.Member.QualifiedName)
            .Distinct()
            .Take(15)
            .ToList();
        if (similar.Count > 0)
            message.AppendLine("Similar members: " + string.Join(", ", similar));

        IReadOnlyList<ApiMember> elsewhere = index.FindMembers(memberName);
        if (elsewhere.Count > 0)
            message.AppendLine("Declared on other types: " + string.Join(", ", elsewhere.Select(member => member.QualifiedName).Distinct().Take(15)));

        return message.ToString();
    }

    public static string TypeNotFound(ApiIndex index, string typeName)
    {
        IReadOnlyList<ApiType> suggestions = index.SuggestTypes(typeName);
        string message = suggestions.Count == 0
            ? $"No type, namespace or enum named \"{typeName}\". Use search_api to look for it."
            : $"No type, namespace or enum named \"{typeName}\". Did you mean: {string.Join(", ", suggestions.Select(type => type.Name))}?";
        return message + DumpNote(index);
    }

    /// <param name="baseClass">When set, only types that inherit from it, directly or indirectly.</param>
    public static string FormatScriptTypes(ApiIndex index, string filter, string baseClass)
    {
        if (index.Scripts is not { } scripts)
            return $"Project scripts are not indexed. Start the server with --script-dir <folder> (repeatable) or set {ServerOptions.ScriptDirsEnv}.";

        string? baseName = baseClass.Length == 0 ? null : index.FindType(baseClass)?.Name ?? baseClass;

        List<ApiType> matching = scripts.Types
            .Where(type => filter.Length == 0 || type.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .Where(type => baseName is null || InheritsFrom(index, type, baseName))
            .ToList();

        if (matching.Count == 0)
        {
            string criteria = string.Join(" and ", new[] { filter.Length > 0 ? $"names containing \"{filter}\"" : "", baseName is not null ? $"inheriting from {baseName}" : "" }.Where(part => part.Length > 0));
            return criteria.Length > 0
                ? $"No script types with {criteria} in {scripts.FileCount} script file(s)."
                : $"No types are declared in the {scripts.FileCount} script file(s) under {string.Join(", ", scripts.Roots)}.";
        }

        var output = new StringBuilder();
        output.AppendLine($"{matching.Count} script type(s) in {matching.Select(type => type.Source!.Path).Distinct().Count()} file(s):");
        foreach (var file in matching.GroupBy(type => type.Source!.Path))
        {
            output.AppendLine().AppendLine($"## {file.Key}");
            foreach (ApiType type in file)
            {
                if (type.Name == ApiType.GlobalNamespace)
                {
                    output.AppendLine($"- global functions and variables (line {type.Source!.Line}): {string.Join(", ", type.Members.Select(member => member.Name).Distinct())}");
                    continue;
                }

                string detail = type.Kind switch
                {
                    ApiTypeKind.Enum => $"{type.EnumValues.Count} values",
                    _ when type.ScriptKeyword is "delegate" or "event" => type.Declaration,
                    _ => $"{type.Members.Count} members",
                };
                string summary = ApiText.Summarize(type.Documentation, 120);
                output.AppendLine($"- {type.ScriptKeyword} {type.Name}{(type.SuperClass.Length > 0 ? " : " + type.SuperClass : "")} (line {type.Source!.Line}; {detail})"
                    + (summary.Length > 0 ? " — " + summary : ""));
            }
        }

        return ApiText.Truncate(output.ToString(), MaxOutputChars, "Pass filter or baseClass to narrow the list.");
    }

    public static string FormatStatus(ApiIndex index, bool scriptsConfigured)
    {
        var output = new StringBuilder();
        output.AppendLine("## API dump");
        if (index.Dump is { } dump)
        {
            output.AppendLine($"Folder: {dump.Directory}");
            if (dump.NewestFileUtc != default)
                output.AppendLine($"Generated: {dump.NewestFileUtc:yyyy-MM-dd HH:mm} UTC (newest file)");

            int CountOf(ApiTypeKind kind) => dump.Types.Count(type => type.Kind == kind);
            output.AppendLine($"Types: {dump.Types.Count:N0} ({CountOf(ApiTypeKind.Type):N0} types, {CountOf(ApiTypeKind.Namespace):N0} namespaces, {CountOf(ApiTypeKind.Enum):N0} enums)");
            output.AppendLine($"Members: {dump.Types.Sum(type => type.Members.Count):N0}");
            AppendProblems(output, "Load errors", dump.LoadErrors);
        }
        else
        {
            output.AppendLine("Not loaded: " + index.DumpUnavailableReason);
        }

        output.AppendLine().AppendLine("## Project scripts");
        if (!scriptsConfigured)
        {
            output.AppendLine($"Not configured. Pass --script-dir <folder> (repeatable) or set {ServerOptions.ScriptDirsEnv} to index the project's own .as files.");
        }
        else if (index.Scripts is { } scripts)
        {
            output.AppendLine($"Folders: {string.Join(", ", scripts.Roots)}");
            string kinds = string.Join(", ", scripts.Types
                .Where(type => type.Name != ApiType.GlobalNamespace)
                .GroupBy(type => type.ScriptKeyword)
                .OrderByDescending(group => group.Count())
                .Select(group => $"{group.Count()} {group.Key}"));
            output.AppendLine($"Files: {scripts.FileCount:N0}; declarations: {(kinds.Length > 0 ? kinds : "none")}");
            if (scripts.NewestFileUtc != default)
                output.AppendLine($"Last change: {scripts.NewestFileUtc:yyyy-MM-dd HH:mm:ss} UTC (re-checked on every call)");
            AppendProblems(output, "Warnings", scripts.Warnings);
        }

        return output.ToString();
    }

    private static bool InheritsFrom(ApiIndex index, ApiType scriptType, string baseName)
    {
        ApiType type = index.FindType(scriptType.Name) ?? scriptType;
        // Also compare each SuperClass name, so a parent missing from the index still counts.
        return type.SuperClass.Equals(baseName, StringComparison.OrdinalIgnoreCase)
            || index.GetAncestors(type).Any(ancestor => ancestor.SuperClass.Equals(baseName, StringComparison.OrdinalIgnoreCase));
    }

    private static void AppendProblems(StringBuilder output, string label, IReadOnlyList<string> problems)
    {
        if (problems.Count == 0)
        {
            output.AppendLine($"{label}: none");
            return;
        }

        output.AppendLine($"{label}: {problems.Count}");
        foreach (string problem in problems.Take(5))
            output.AppendLine("- " + problem);
    }

    private static string DumpNote(ApiIndex index) =>
        index.DumpUnavailableReason is { } reason ? $"\n(Only project scripts were searched. The engine API dump is not loaded: {reason})" : "";

    private static string DescribeHit(SearchHit hit)
    {
        string location = hit.Source is { } source ? $"  [{source}]" : "";
        if (hit.EnumValue is not null)
            return $"enum value  {hit.Type.Name}::{hit.EnumValue}{location}";

        if (hit.Member is null)
        {
            string parent = hit.Type.SuperClass.Length > 0 ? $" : {hit.Type.SuperClass}" : "";
            string summary = ApiText.Summarize(hit.Type.Documentation, 120);
            return $"{hit.Type.KindLabel}  {hit.Type.Name}{parent}" + (summary.Length > 0 ? $" — {summary}" : "") + location;
        }

        ApiMember member = hit.Member;
        string label = member.Kind == ApiMemberKind.Function ? "function" : "property";
        if (member.IsStatic && member.DeclaringType.Kind != ApiTypeKind.Namespace)
            label = "static " + label;
        if (member.Source is not null)
            label = "script " + label;

        string overloads = hit.Overloads > 1 ? $"  (+{hit.Overloads - 1} overload(s))" : "";
        return $"{label}  {member.QualifiedName}  →  {member.Declaration}{overloads}{location}";
    }

    /// <returns>The number of members written.</returns>
    private static int AppendMembers(StringBuilder output, ApiType type, string filter, bool includeDocs, string? heading)
    {
        List<ApiMember> members = type.Members
            .Where(member => filter.Length == 0 || member.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (heading is not null && members.Count > 0)
            output.AppendLine().AppendLine($"## {heading}");

        // Properties before functions, each grouped by category in declaration order.
        foreach (var group in members.OrderBy(member => member.Kind == ApiMemberKind.Function).GroupBy(member => (member.Kind, member.Category)))
        {
            output.AppendLine().AppendLine("### " + GroupTitle(group.Key.Kind, group.Key.Category));
            foreach (ApiMember member in group)
            {
                output.Append("- `");
                if (member.IsStatic && type.Kind != ApiTypeKind.Namespace)
                    output.Append("static ");
                output.Append(member.Declaration).Append('`');

                if (member.Source is { } source)
                    output.Append(type.Source?.Path == source.Path ? $" (line {source.Line})" : $" [{source}]");

                if (includeDocs)
                {
                    output.AppendLine();
                    if (member.Documentation.Length > 0)
                        output.AppendLine(ApiText.Indent(member.Documentation, "  "));
                }
                else
                {
                    string summary = ApiText.Summarize(member.Documentation, 140);
                    output.AppendLine(summary.Length > 0 ? " — " + summary : "");
                }
            }
        }

        return members.Count;
    }

    private static string GroupTitle(ApiMemberKind kind, string category)
    {
        string defaultTitle = kind == ApiMemberKind.Function ? "Functions" : "Properties";
        return category is "Functions" or "Static Functions" or "Variables" or "Static Variables"
            ? (category.StartsWith("Static", StringComparison.Ordinal) ? "Static " : "") + defaultTitle
            : $"{defaultTitle}: {category}";
    }

    private static string RenderDeclarations(string title, IReadOnlyList<ApiMember> members)
    {
        var output = new StringBuilder();
        output.AppendLine(title);

        foreach (ApiMember member in members.Take(MaxListedDeclarations))
        {
            output.AppendLine().AppendLine($"## `{(member.IsStatic && member.DeclaringType.Kind != ApiTypeKind.Namespace ? "static " : "")}{member.Declaration}`");
            string kind = member.Kind == ApiMemberKind.Function ? "function" : "property";
            string origin = member.Source is { } source ? $" · declared in {source}" : "";
            output.AppendLine($"{kind} of {member.DeclaringType.Name} ({member.DeclaringType.KindLabel}) · category: {member.Category} · script name: {member.QualifiedName}{origin}");
            if (member.Documentation.Length > 0)
                output.AppendLine().AppendLine(member.Documentation);
        }

        if (members.Count > MaxListedDeclarations)
            output.AppendLine().AppendLine($"(+{members.Count - MaxListedDeclarations} more declarations; pass typeName to narrow it down.)");

        return ApiText.Truncate(output.ToString(), MaxOutputChars, "Pass typeName to narrow it down.");
    }

    private static string SimilarMembers(ApiIndex index, string memberName)
    {
        List<string> similar = index.Search(memberName, ApiSearchKind.Any, 10)
            .Where(hit => hit.Member is not null)
            .Select(hit => hit.Member!.QualifiedName)
            .ToList();
        return similar.Count == 0 ? "Use search_api to look for it." : "Similar: " + string.Join(", ", similar);
    }
}
