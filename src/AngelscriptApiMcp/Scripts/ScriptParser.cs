using System.Text;
using System.Text.RegularExpressions;
using AngelscriptApiMcp.Api;

namespace AngelscriptApiMcp.Scripts;

/// <summary>
/// Extracts declarations from Angelscript source: classes, structs, enums, delegates, events,
/// namespaces, and the functions and properties inside them.
/// </summary>
/// <remarks>
/// This is a declaration scanner, not a compiler. Function bodies and initializers are skipped by
/// bracket matching, and anything unrecognised is stepped over, so malformed or unusual code can lose
/// a declaration but never stops the rest of the file from being read.
/// </remarks>
public static partial class ScriptParser
{
    /// <param name="text">File contents.</param>
    /// <param name="path">Path shown to users, e.g. <c>Script/Pickups/Pickup.as</c>.</param>
    public static IReadOnlyList<ApiType> Parse(string text, string path) =>
        new DeclarationReader(ScriptTokenizer.Tokenize(text), path).ReadFile();

    [GeneratedRegex(@"\bCategory\s*=\s*""(?<category>[^""]*)""")]
    private static partial Regex CategorySpecifier();

    private sealed class DeclarationReader(List<Token> tokens, string path)
    {
        private static readonly HashSet<string> SpecifierMacros = ["UCLASS", "USTRUCT", "UENUM", "UINTERFACE", "UFUNCTION", "UPROPERTY", "UDELEGATE"];
        private static readonly HashSet<string> TypeModifiers = ["shared", "abstract", "final", "external"];
        private static readonly HashSet<string> AccessKeywords = ["private", "protected", "public"];
        private static readonly HashSet<string> SkippedStatements = ["import", "funcdef", "typedef", "access", "default"];

        private readonly List<ApiType> _types = [];
        private readonly Dictionary<string, (List<ApiMember> Members, SourceLocation Source)> _namespaces = new(StringComparer.Ordinal);
        private int _position;

        private Token Current => tokens[_position];

        private bool AtEnd => Current.Kind == TokenKind.End;

        public List<ApiType> ReadFile()
        {
            ReadScope(namespaceName: "", insideBraces: false);

            foreach ((string name, (List<ApiMember> members, SourceLocation source)) in _namespaces)
            {
                bool isGlobal = name.Length == 0;
                _types.Add(new ApiType(
                    isGlobal ? ApiType.GlobalNamespace : name, "", "", members, [], source,
                    isGlobal ? "" : "namespace " + name, "namespace"));
            }

            return _types;
        }

        private void ReadScope(string namespaceName, bool insideBraces)
        {
            while (!AtEnd)
            {
                if (Current.Is("}"))
                {
                    Advance();
                    if (insideBraces)
                        return;
                    continue;
                }

                if (Current.Is(";"))
                {
                    Advance();
                    continue;
                }

                string documentation = Current.Comment;
                string specifiers = ReadSpecifiers();
                while (Current.Kind == TokenKind.Identifier && TypeModifiers.Contains(Current.Text))
                    Advance();

                if (Current.Is("class") || Current.Is("struct"))
                    ReadClass(documentation, specifiers);
                else if (Current.Is("enum"))
                    ReadEnum(documentation, specifiers);
                else if (Current.Is("namespace"))
                    ReadNamespace(namespaceName);
                else if (Current.Is("delegate") || Current.Is("event"))
                    ReadDelegate(documentation);
                else if (Current.Kind == TokenKind.Identifier && SkippedStatements.Contains(Current.Text))
                    SkipStatement();
                else if (Current.Kind == TokenKind.Identifier)
                {
                    string prefix = "";
                    if (Current.Is("mixin"))
                    {
                        Advance();
                        prefix = "mixin ";
                    }

                    if (ReadMember(documentation, specifiers, prefix, isStatic: true) is { } member)
                        AddToNamespace(namespaceName, member);
                }
                else
                {
                    Advance();
                }
            }
        }

        private void ReadClass(string documentation, string specifiers)
        {
            string keyword = Advance().Text;
            if (Current.Kind != TokenKind.Identifier)
                return;

            Token name = Advance();
            while (Current.Kind == TokenKind.Identifier && TypeModifiers.Contains(Current.Text))
                Advance();

            var bases = new List<string>();
            if (Current.Is(":"))
            {
                Advance();
                int start = _position;
                while (!AtEnd && !Current.Is("{") && !Current.Is(";"))
                    Advance();
                bases.AddRange(Text(start, _position).Split(',').Select(part => part.Trim()).Where(part => part.Length > 0));
            }

            if (!Current.Is("{"))
            {
                if (Current.Is(";"))
                    Advance(); // forward declaration
                return;
            }

            Advance();
            var members = new List<ApiMember>();
            while (!AtEnd && !Current.Is("}"))
            {
                if (Current.Is(";"))
                {
                    Advance();
                    continue;
                }

                string memberDocumentation = Current.Comment;
                string memberSpecifiers = ReadSpecifiers();
                string prefix = "";
                bool isStatic = false;

                while (true)
                {
                    if (Current.Is("access") && Peek(1).Is(":") && Peek(2).Kind == TokenKind.Identifier)
                    {
                        Advance();
                        Advance();
                        prefix += "access:" + Advance().Text + " ";
                    }
                    else if (Current.Kind == TokenKind.Identifier && AccessKeywords.Contains(Current.Text))
                    {
                        prefix += Advance().Text + " ";
                    }
                    else if (Current.Is("static"))
                    {
                        Advance();
                        isStatic = true;
                    }
                    else
                    {
                        string more = ReadSpecifiers();
                        if (more.Length == 0)
                            break;
                        memberSpecifiers = Join(memberSpecifiers, more);
                    }
                }

                if (Current.Is("default") || Current.Is("access"))
                    SkipStatement();
                else if (Current.Is("class") || Current.Is("struct") || Current.Is("enum"))
                    SkipStatement(); // nested types are not valid Angelscript; step over them
                else if (Current.Kind == TokenKind.Identifier)
                {
                    if (ReadMember(memberDocumentation, memberSpecifiers, prefix, isStatic) is { } member)
                        members.Add(member);
                }
                else if (!Current.Is("}"))
                {
                    Advance();
                }
            }

            if (Current.Is("}"))
                Advance();

            string declaration = $"{keyword} {name.Text}" + (bases.Count > 0 ? " : " + string.Join(", ", bases) : "");
            _types.Add(new ApiType(
                name.Text, bases.FirstOrDefault() ?? "", documentation, members, [],
                new SourceLocation(path, name.Line), Join(specifiers, declaration), keyword));
        }

        private void ReadEnum(string documentation, string specifiers)
        {
            Advance();
            if (Current.Is("class"))
                Advance();
            if (Current.Kind != TokenKind.Identifier)
                return;

            Token name = Advance();
            while (!AtEnd && !Current.Is("{") && !Current.Is(";"))
                Advance(); // underlying type, if any

            if (!Current.Is("{"))
            {
                if (Current.Is(";"))
                    Advance();
                return;
            }

            Advance();
            var values = new List<string>();
            bool expectValue = true;
            int depth = 0;
            while (!AtEnd)
            {
                Token token = Advance();
                if (depth == 0 && token.Is("}"))
                    break;

                if (token.Is("(") || token.Is("{") || token.Is("["))
                    depth++;
                else if (token.Is(")") || token.Is("}") || token.Is("]"))
                    depth--;
                else if (depth == 0 && token.Is(","))
                    expectValue = true;
                else if (depth == 0 && expectValue && token.Kind == TokenKind.Identifier)
                {
                    values.Add(token.Text);
                    expectValue = false;
                }
            }

            _types.Add(new ApiType(
                name.Text, "", documentation, [], values,
                new SourceLocation(path, name.Line), Join(specifiers, "enum " + name.Text), "enum"));
        }

        private void ReadNamespace(string outerNamespace)
        {
            Advance();
            var parts = new List<string>();
            while (Current.Kind == TokenKind.Identifier)
            {
                parts.Add(Advance().Text);
                if (!Current.Is("::"))
                    break;
                Advance();
            }

            if (parts.Count == 0 || !Current.Is("{"))
                return;

            Advance();
            string name = string.Join("::", parts);
            ReadScope(outerNamespace.Length > 0 ? outerNamespace + "::" + name : name, insideBraces: true);
        }

        private void ReadDelegate(string documentation)
        {
            string keyword = Advance().Text;
            int start = _position;
            SkipToStatementEnd();
            int end = _position;
            if (Current.Is(";"))
                Advance();

            for (int i = start; i < end; i++)
            {
                if (tokens[i].Is("(") && i > start && tokens[i - 1].Kind == TokenKind.Identifier)
                {
                    Token name = tokens[i - 1];
                    _types.Add(new ApiType(
                        name.Text, "", documentation, [], [],
                        new SourceLocation(path, name.Line), $"{keyword} {Text(start, end)}", keyword));
                    return;
                }
            }
        }

        /// <summary>Reads one function or property declaration; returns null for anything else.</summary>
        private ApiMember? ReadMember(string documentation, string specifiers, string prefix, bool isStatic)
        {
            int start = _position;
            while (!AtEnd)
            {
                if (Current.Is("("))
                {
                    if (_position == start || tokens[_position - 1].Kind != TokenKind.Identifier)
                    {
                        SkipBalanced("(", ")");
                        continue;
                    }

                    Token name = tokens[_position - 1];
                    string returnType = Text(start, _position - 1);
                    int parametersStart = _position;
                    SkipBalanced("(", ")");
                    string parameters = Text(parametersStart, _position);

                    int qualifiersStart = _position;
                    while (!AtEnd && !Current.Is("{") && !Current.Is(";") && !Current.Is("}"))
                        Advance();
                    string qualifiers = Text(qualifiersStart, _position);

                    if (Current.Is("{"))
                        SkipBalanced("{", "}");
                    else if (Current.Is(";"))
                        Advance();

                    if (returnType.Length == 0)
                        return null; // constructor, or a statement that only looks like a call

                    string declaration = $"{prefix}{returnType} {name.Text}{parameters}" + (qualifiers.Length > 0 ? " " + qualifiers : "");
                    return CreateMember(name, ApiMemberKind.Function, Join(specifiers, declaration), documentation, specifiers, isStatic);
                }

                if (Current.Is(";") || Current.Is("="))
                {
                    if (_position == start || tokens[_position - 1].Kind != TokenKind.Identifier)
                    {
                        SkipStatement();
                        return null;
                    }

                    Token name = tokens[_position - 1];
                    string type = Text(start, _position - 1);
                    string initializer = "";
                    if (Current.Is("="))
                    {
                        Advance();
                        int initializerStart = _position;
                        SkipToStatementEnd();
                        initializer = Shorten(Text(initializerStart, _position), 80);
                    }

                    if (Current.Is(";"))
                        Advance();
                    if (type.Length == 0)
                        return null;

                    string declaration = $"{prefix}{type} {name.Text}" + (initializer.Length > 0 ? " = " + initializer : "");
                    return CreateMember(name, ApiMemberKind.Property, Join(specifiers, declaration), documentation, specifiers, isStatic);
                }

                if (Current.Is("{"))
                {
                    SkipBalanced("{", "}");
                    return null;
                }

                if (Current.Is("}"))
                    return null; // end of the enclosing scope; leave it for the caller

                Advance();
            }

            return null;
        }

        private ApiMember CreateMember(Token name, ApiMemberKind kind, string declaration, string documentation, string specifiers, bool isStatic)
        {
            Match category = CategorySpecifier().Match(specifiers);
            string defaultCategory = (isStatic ? "Static " : "") + (kind == ApiMemberKind.Function ? "Functions" : "Variables");
            return new ApiMember(
                name.Text, kind, declaration, documentation,
                category.Success ? category.Groups["category"].Value : defaultCategory,
                isStatic, new SourceLocation(path, name.Line));
        }

        private void AddToNamespace(string namespaceName, ApiMember member)
        {
            if (!_namespaces.TryGetValue(namespaceName, out var entry))
            {
                entry = ([], member.Source!);
                _namespaces.Add(namespaceName, entry);
            }

            entry.Members.Add(member);
        }

        private string ReadSpecifiers()
        {
            string specifiers = "";
            while (Current.Kind == TokenKind.Identifier && SpecifierMacros.Contains(Current.Text) && Peek(1).Is("("))
            {
                int start = _position;
                Advance();
                SkipBalanced("(", ")");
                specifiers = Join(specifiers, Text(start, _position));
            }

            return specifiers;
        }

        private void SkipStatement()
        {
            SkipToStatementEnd();
            if (Current.Is(";"))
                Advance();
        }

        /// <summary>Stops at the <c>;</c> ending the statement, or at a <c>}</c> closing the enclosing scope.</summary>
        private void SkipToStatementEnd()
        {
            int depth = 0;
            while (!AtEnd)
            {
                if (depth == 0 && (Current.Is(";") || Current.Is("}")))
                    return;

                if (Current.Is("(") || Current.Is("{") || Current.Is("["))
                    depth++;
                else if (Current.Is(")") || Current.Is("}") || Current.Is("]"))
                    depth--;

                Advance();
            }
        }

        private void SkipBalanced(string open, string close)
        {
            int depth = 0;
            while (!AtEnd)
            {
                Token token = Advance();
                if (token.Is(open))
                    depth++;
                else if (token.Is(close) && --depth <= 0)
                    return;
            }
        }

        private Token Advance()
        {
            Token token = Current;
            if (!AtEnd)
                _position++;
            return token;
        }

        private Token Peek(int offset) => tokens[Math.Min(_position + offset, tokens.Count - 1)];

        /// <summary>Source text of tokens [start, end), with whitespace normalised to single spaces.</summary>
        private string Text(int start, int end)
        {
            var text = new StringBuilder();
            for (int i = start; i < end; i++)
            {
                Token token = tokens[i];
                bool space = text.Length > 0 && token.SpaceBefore
                    && !(token.Kind == TokenKind.Symbol && token.Text is ")" or "]" or "," or ";" or "." or "::" or ">")
                    && !(tokens[i - 1].Kind == TokenKind.Symbol && tokens[i - 1].Text is "(" or "[" or "." or "::" or "<");
                if (space)
                    text.Append(' ');
                text.Append(token.Text);
            }

            return text.ToString();
        }

        private static string Join(string left, string right) =>
            left.Length == 0 ? right : right.Length == 0 ? left : left + " " + right;

        private static string Shorten(string text, int maxLength) =>
            text.Length <= maxLength ? text : text[..maxLength] + "…";
    }
}
