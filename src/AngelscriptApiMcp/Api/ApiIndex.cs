namespace AngelscriptApiMcp.Api;

public enum ApiSearchKind
{
    Any,
    Type,
    Namespace,
    Enum,
    Function,
    Property,
}

public enum ApiOrigin
{
    Any,

    /// <summary>From the engine dump.</summary>
    Engine,

    /// <summary>Declared in the project's own .as files.</summary>
    Script,
}

/// <param name="Type">The matched type, or the type that declares <see cref="Member"/> or <see cref="EnumValue"/>.</param>
/// <param name="Overloads">How many same-named members were folded into this hit.</param>
public sealed record SearchHit(ApiType Type, ApiMember? Member, string? EnumValue, int Score, int Overloads = 1)
{
    /// <summary>File and line of the matched declaration when it comes from a project script.</summary>
    public SourceLocation? Source => Member is not null ? Member.Source : Type.Source;
}

/// <summary>An in-memory, case-insensitive index over engine and script types.</summary>
public sealed class ApiIndex
{
    /// <summary>Unreal type-name prefixes, so that "Actor" finds AActor and "Vector" finds FVector.</summary>
    private const string TypePrefixes = "UAFEIT";

    private readonly Dictionary<string, ApiType> _types = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ApiMember>> _membersByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ApiType>> _subclasses = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="types">Engine and script types. Types that share a name (such as the Global namespace, which both can add to) are merged.</param>
    public ApiIndex(IEnumerable<ApiType> types, DumpInfo? dump = null, ScriptSnapshot? scripts = null, string? dumpUnavailableReason = null)
    {
        int memberCount = 0;
        foreach (ApiType type in MergeSameNamed(types))
        {
            _types.Add(type.Name, type);
            memberCount += type.Members.Count;
            foreach (ApiMember member in type.Members)
                GetOrAdd(_membersByName, member.Name).Add(member);
            if (type.SuperClass.Length > 0)
                GetOrAdd(_subclasses, type.SuperClass).Add(type);
        }

        MemberCount = memberCount;
        Dump = dump;
        Scripts = scripts;
        DumpUnavailableReason = dumpUnavailableReason;
    }

    public DumpInfo? Dump { get; }

    public ScriptSnapshot? Scripts { get; }

    /// <summary>Why the engine dump is not part of this index, when it is not.</summary>
    public string? DumpUnavailableReason { get; }

    public int MemberCount { get; }

    public IReadOnlyCollection<ApiType> Types => _types.Values;

    public ApiType? FindType(string name)
    {
        name = name.Trim();
        if (_types.TryGetValue(name, out ApiType? type))
            return type;

        foreach (char prefix in TypePrefixes)
        {
            if (_types.TryGetValue(prefix + name, out type))
                return type;
        }

        return null;
    }

    /// <summary>Parent, grandparent and so on, stopping at the first parent that is not indexed.</summary>
    public IReadOnlyList<ApiType> GetAncestors(ApiType type)
    {
        var ancestors = new List<ApiType>();
        var seen = new HashSet<ApiType> { type };
        string next = type.SuperClass;
        while (next.Length > 0 && _types.TryGetValue(next, out ApiType? parent) && seen.Add(parent))
        {
            ancestors.Add(parent);
            next = parent.SuperClass;
        }

        return ancestors;
    }

    public IReadOnlyList<ApiType> GetSubclasses(ApiType type) =>
        _subclasses.TryGetValue(type.Name, out List<ApiType>? subclasses) ? subclasses : [];

    public IReadOnlyList<ApiMember> FindMembers(string name) =>
        _membersByName.TryGetValue(name.Trim(), out List<ApiMember>? members) ? members : [];

    public IReadOnlyList<ApiType> SuggestTypes(string name, int limit = 8)
    {
        name = name.Trim();
        List<ApiType> suggestions = RankTypes(name, limit);
        return suggestions.Count > 0 || name.Length <= 5 ? suggestions : RankTypes(name[..5], limit);
    }

    /// <summary>
    /// Searches type names, member names, enum values and, with a lower score, documentation.
    /// Accepts a name fragment, a qualified <c>Type.Member</c> or <c>Namespace::Member</c> (which also
    /// searches inherited members), or several words that must all appear in a name or its docs.
    /// </summary>
    public IReadOnlyList<SearchHit> Search(string query, ApiSearchKind kind = ApiSearchKind.Any, int limit = 25, ApiOrigin origin = ApiOrigin.Any)
    {
        query = query.Trim();
        if (query.Length == 0 || limit <= 0)
            return [];

        var hits = new List<SearchHit>();
        if (TrySplitQualified(query, out string typePart, out string memberPart))
        {
            string[] memberTokens = [memberPart];
            foreach (ApiType owner in ResolveOwners(typePart))
            {
                AddMemberHits(hits, owner, memberTokens, kind);
                foreach (ApiType ancestor in GetAncestors(owner))
                    AddMemberHits(hits, ancestor, memberTokens, kind);
            }
        }
        else
        {
            string[] tokens = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            foreach (ApiType type in _types.Values)
            {
                if (Allows(kind, type, member: null, isEnumValue: false))
                {
                    int score = Score(type.Name, type.Documentation, tokens, isTypeName: true);
                    if (score > 0)
                        hits.Add(new SearchHit(type, null, null, score + 1)); // a type outranks an equally good member
                }

                if (kind is ApiSearchKind.Any or ApiSearchKind.Enum)
                {
                    foreach (string value in type.EnumValues)
                    {
                        int score = Score(value, "", tokens, isTypeName: false);
                        if (score > 0)
                            hits.Add(new SearchHit(type, null, value, score));
                    }
                }

                AddMemberHits(hits, type, tokens, kind);
            }
        }

        return hits
            .Where(hit => origin == ApiOrigin.Any || (origin == ApiOrigin.Script) == (hit.Source is not null))
            .GroupBy(hit => (hit.Type, Name: hit.Member?.Name ?? hit.EnumValue))
            .Select(group => group.MaxBy(hit => hit.Score)! with { Overloads = group.Count() })
            .OrderByDescending(hit => hit.Score)
            .ThenBy(hit => (hit.Member?.Name ?? hit.EnumValue ?? hit.Type.Name).Length)
            .ThenBy(hit => hit.Type.Name, StringComparer.Ordinal)
            .Take(limit)
            .ToList();
    }

    /// <summary>Scores how well <paramref name="name"/> matches a single search token (0 = no match).</summary>
    public static int NameScore(string name, string token, bool isTypeName)
    {
        if (token.Length == 0)
            return 0;
        if (name.Equals(token, StringComparison.OrdinalIgnoreCase))
            return 100;

        bool hasTypePrefix = isTypeName && name.Length > token.Length && TypePrefixes.Contains(name[0]);
        if (hasTypePrefix && name.Length == token.Length + 1 && name.AsSpan(1).Equals(token, StringComparison.OrdinalIgnoreCase))
            return 95;

        int extra = name.Length - token.Length;
        if (name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
            return 70 - Math.Min(20, extra);
        if (hasTypePrefix && name.AsSpan(1).StartsWith(token, StringComparison.OrdinalIgnoreCase))
            return 65 - Math.Min(20, extra);
        if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
            return 40 - Math.Min(20, extra / 2);

        return 0;
    }

    private static IEnumerable<ApiType> MergeSameNamed(IEnumerable<ApiType> types) =>
        types
            .GroupBy(type => type.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Skip(1).Any() ? Merge(group.ToList()) : group.First())
            .OrderBy(type => type.Name, StringComparer.Ordinal);

    /// <summary>
    /// Combines same-named types into one, e.g. engine and script functions in the Global namespace, or
    /// one script namespace spread over several files. Members are copied so the inputs stay untouched.
    /// </summary>
    private static ApiType Merge(IReadOnlyList<ApiType> parts)
    {
        ApiType header = parts.FirstOrDefault(part => !part.IsScript) ?? parts[0];
        return new ApiType(
            header.Name,
            parts.Select(part => part.SuperClass).FirstOrDefault(superClass => superClass.Length > 0) ?? "",
            parts.Select(part => part.Documentation).FirstOrDefault(documentation => documentation.Length > 0) ?? "",
            parts.SelectMany(part => part.Members).Select(member => member.Detach()).ToList(),
            parts.SelectMany(part => part.EnumValues).Distinct().ToList(),
            header.Source,
            header.Declaration,
            header.ScriptKeyword);
    }

    private static int Score(string name, string documentation, string[] tokens, bool isTypeName)
    {
        if (tokens.Length == 1)
        {
            int nameScore = NameScore(name, tokens[0], isTypeName);
            if (nameScore > 0)
                return nameScore;
            return tokens[0].Length >= 4 && documentation.Contains(tokens[0], StringComparison.OrdinalIgnoreCase) ? 5 : 0;
        }

        int inName = 0;
        foreach (string token in tokens)
        {
            if (name.Contains(token, StringComparison.OrdinalIgnoreCase))
                inName++;
            else if (!documentation.Contains(token, StringComparison.OrdinalIgnoreCase))
                return 0;
        }

        return 10 + inName * 10;
    }

    private static void AddMemberHits(List<SearchHit> hits, ApiType type, string[] tokens, ApiSearchKind kind)
    {
        foreach (ApiMember member in type.Members)
        {
            if (!Allows(kind, type, member, isEnumValue: false))
                continue;

            int score = Score(member.Name, member.Documentation, tokens, isTypeName: false);
            if (score > 0)
                hits.Add(new SearchHit(type, member, null, score));
        }
    }

    private static bool Allows(ApiSearchKind kind, ApiType type, ApiMember? member, bool isEnumValue) => kind switch
    {
        ApiSearchKind.Any => true,
        ApiSearchKind.Type => member is null && !isEnumValue && type.Kind == ApiTypeKind.Type,
        ApiSearchKind.Namespace => member is null && !isEnumValue && type.Kind == ApiTypeKind.Namespace,
        ApiSearchKind.Enum => member is null && (isEnumValue || type.Kind == ApiTypeKind.Enum),
        ApiSearchKind.Function => member?.Kind == ApiMemberKind.Function,
        ApiSearchKind.Property => member?.Kind == ApiMemberKind.Property,
        _ => false,
    };

    private IEnumerable<ApiType> ResolveOwners(string typePart)
    {
        if (FindType(typePart) is { } exact)
            return [exact];

        return RankTypes(typePart, limit: 5);
    }

    private List<ApiType> RankTypes(string name, int limit) =>
        _types.Values
            .Select(type => (Type: type, Score: NameScore(type.Name, name, isTypeName: true)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Type.Name.Length)
            .Take(limit)
            .Select(candidate => candidate.Type)
            .ToList();

    private static bool TrySplitQualified(string query, out string typePart, out string memberPart)
    {
        typePart = memberPart = "";
        if (query.Contains(' '))
            return false;

        int colons = query.LastIndexOf("::", StringComparison.Ordinal);
        int dot = query.LastIndexOf('.');
        int split = Math.Max(colons, dot);
        int separatorLength = split >= 0 && split == colons ? 2 : 1;
        if (split <= 0 || split + separatorLength >= query.Length)
            return false;

        typePart = query[..split];
        memberPart = query[(split + separatorLength)..];
        return true;
    }

    private static List<TValue> GetOrAdd<TValue>(Dictionary<string, List<TValue>> map, string key)
    {
        if (!map.TryGetValue(key, out List<TValue>? list))
        {
            list = [];
            map.Add(key, list);
        }

        return list;
    }
}
