using System.Text;

namespace AngelscriptApiMcp.Scripts;

internal enum TokenKind
{
    Identifier,
    Number,
    String,
    Symbol,
    End,
}

/// <param name="SpaceBefore">Whitespace or a comment separated this token from the previous one.</param>
/// <param name="Comment">The comment block directly above this token, if any.</param>
internal readonly record struct Token(TokenKind Kind, string Text, int Line, bool SpaceBefore, string Comment)
{
    public bool Is(string text) => Kind is TokenKind.Identifier or TokenKind.Symbol && Text == text;
}

/// <summary>
/// Splits Angelscript source into tokens, skipping comments and preprocessor lines. A comment block
/// that ends on the line directly above a token (with no blank line between) is attached to that
/// token as its documentation; a comment on the same line as the previous token is not.
/// </summary>
internal static class ScriptTokenizer
{
    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var comment = new StringBuilder();
        int commentEndLine = int.MinValue;
        int lastTokenLine = 0;
        int line = 1;
        int i = 0;
        bool spaceBefore = false;
        bool atLineStart = true;

        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\n')
            {
                line++;
                i++;
                spaceBefore = true;
                atLineStart = true;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                i++;
                spaceBefore = true;
                continue;
            }

            if (c == '#' && atLineStart)
            {
                // #if EDITOR, #endif, ...: both branches are indexed.
                while (i < text.Length && text[i] != '\n')
                    i++;
                continue;
            }

            atLineStart = false;
            char next = i + 1 < text.Length ? text[i + 1] : '\0';

            if (c == '/' && next == '/')
            {
                int end = text.IndexOf('\n', i);
                if (end < 0)
                    end = text.Length;
                AddComment(text[(i + 2)..end].TrimStart('/').Trim(), line, line);
                i = end;
                spaceBefore = true;
                continue;
            }

            if (c == '/' && next == '*')
            {
                int end = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (end < 0)
                    end = text.Length;
                string body = text[(i + 2)..end];
                int startLine = line;
                line += body.Count(ch => ch == '\n');
                AddComment(CleanBlockComment(body), startLine, line);
                i = Math.Min(text.Length, end + 2);
                spaceBefore = true;
                continue;
            }

            int tokenLine = line;
            int start = i;
            TokenKind kind;

            if (c is '"' or '\'')
            {
                kind = TokenKind.String;
                if (c == '"' && next == '"' && i + 2 < text.Length && text[i + 2] == '"')
                {
                    int end = text.IndexOf("\"\"\"", i + 3, StringComparison.Ordinal);
                    i = end < 0 ? text.Length : end + 3;
                }
                else
                {
                    i++;
                    while (i < text.Length && text[i] != c && text[i] != '\n')
                        i += text[i] == '\\' ? 2 : 1;
                    i = Math.Min(text.Length, i + 1);
                }

                line += text.AsSpan(start, i - start).Count('\n');
            }
            else if (char.IsDigit(c) || (c == '.' && char.IsDigit(next)))
            {
                kind = TokenKind.Number;
                i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'
                       || (text[i] is '+' or '-' && text[i - 1] is 'e' or 'E')))
                {
                    i++;
                }
            }
            else if (char.IsLetter(c) || c == '_')
            {
                kind = TokenKind.Identifier;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
                    i++;
            }
            else
            {
                kind = TokenKind.Symbol;
                i += c == ':' && next == ':' ? 2 : 1;
            }

            bool documented = comment.Length > 0 && commentEndLine >= tokenLine - 1;
            tokens.Add(new Token(kind, text[start..i], tokenLine, spaceBefore, documented ? comment.ToString() : ""));
            comment.Clear();
            lastTokenLine = line;
            spaceBefore = false;
        }

        tokens.Add(new Token(TokenKind.End, "", line, true, ""));
        return tokens;

        void AddComment(string content, int startLine, int endLine)
        {
            if (startLine == lastTokenLine)
                return; // trailing comment on the previous token's line

            if (comment.Length > 0 && startLine > commentEndLine + 1)
                comment.Clear(); // a blank line ends the previous block

            if (content.Length > 0)
            {
                if (comment.Length > 0)
                    comment.Append('\n');
                comment.Append(content);
            }

            commentEndLine = endLine;
        }
    }

    private static string CleanBlockComment(string body)
    {
        List<string> lines = body.Split('\n')
            .Select(line => line.Trim())
            .Select(line => line.StartsWith('*') ? line[1..].TrimStart() : line)
            .ToList();

        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        return string.Join('\n', lines);
    }
}
