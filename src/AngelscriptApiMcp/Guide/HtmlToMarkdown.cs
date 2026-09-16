using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AngelscriptApiMcp.Guide;

/// <summary>
/// Turns a page of the Angelscript guide (a Zola static site) into Markdown-style text. Deliberately
/// small: it handles the elements those pages use, not arbitrary HTML.
/// </summary>
public static partial class HtmlToMarkdown
{
    private const char PlaceholderMark = '';
    private const char QuoteStart = '';
    private const char QuoteEnd = '';

    public static (string Title, string Markdown) ConvertPage(string html, Uri pageUrl)
    {
        string content = InnerHtml(html, "article") ?? InnerHtml(html, "main") ?? InnerHtml(html, "body") ?? html;
        content = HeadingAnchor().Replace(content, "");

        Match heading = Heading().Match(content);
        string title = heading.Success
            ? InlineText(heading.Groups["body"].Value)
            : TitleTag().Match(html) is { Success: true } titleTag ? InlineText(titleTag.Groups["body"].Value) : "";

        return (title, Convert(content, pageUrl));
    }

    public static string Convert(string html, Uri? baseUrl = null)
    {
        // Finished Markdown fragments are swapped out for placeholders so later passes cannot touch them.
        var fragments = new List<string>();
        string Protect(string fragment)
        {
            fragments.Add(fragment);
            return $"{PlaceholderMark}{fragments.Count - 1}{PlaceholderMark}";
        }

        html = Comment().Replace(html, "");
        html = ScriptOrStyle().Replace(html, "");
        html = HeadingAnchor().Replace(html, "");
        html = ReplaceCodeBlocks(html, code => "\n\n" + Protect("```angelscript\n" + code + "\n```") + "\n\n");
        html = Preformatted().Replace(html, match => "\n\n" + Protect("```\n" + Decode(StripTags(match.Groups["body"].Value)).Trim('\n') + "\n```") + "\n\n");

        // Outside code, source whitespace is insignificant.
        html = Whitespace().Replace(html, " ");

        html = InlineCode().Replace(html, match => Protect("`" + Decode(StripTags(match.Groups["body"].Value)) + "`"));
        html = Link().Replace(html, match =>
        {
            string text = InlineText(match.Groups["body"].Value);
            string href = Decode(match.Groups["href"].Value);
            if (text.Length == 0)
                return "";
            if (href.Length == 0 || href.StartsWith('#'))
                return text;
            string url = baseUrl is not null && Uri.TryCreate(baseUrl, href, out Uri? absolute) ? absolute.AbsoluteUri : href;
            return Protect($"[{text}]({url})");
        });
        html = ImageWithAlt().Replace(html, match => Protect($"[image: {Decode(match.Groups["alt"].Value)}]"));
        html = Heading().Replace(html, match =>
            $"\n\n{new string('#', match.Groups["level"].Value[0] - '0')} {InlineText(match.Groups["body"].Value)}\n\n");

        html = Bold().Replace(html, "**");
        html = Italic().Replace(html, "*");
        html = ListItemOpen().Replace(html, "\n- ");
        html = TableRowOpen().Replace(html, "\n|");
        html = TableCellClose().Replace(html, " |");
        html = LineBreak().Replace(html, "\n");
        html = Rule().Replace(html, "\n\n---\n\n");
        html = BlockquoteOpen().Replace(html, $"\n\n{QuoteStart}");
        html = BlockquoteClose().Replace(html, $"{QuoteEnd}\n\n");
        html = ParagraphBoundary().Replace(html, "\n\n");
        html = BlockBoundary().Replace(html, "\n");

        string text = Decode(StripTags(html));
        text = string.Join('\n', text.Split('\n').Select(line => Whitespace().Replace(line, " ").Trim()));
        text = ExtraBlankLines().Replace(text, "\n\n");
        text = Quote().Replace(text, match =>
            string.Join('\n', match.Groups["body"].Value.Trim('\n').Split('\n').Select(line => line.Length == 0 ? ">" : "> " + line)));

        // Fragments can contain other fragments (inline code inside a link).
        for (int depth = 0; depth < 4 && text.Contains(PlaceholderMark); depth++)
            text = Placeholder().Replace(text, match => fragments[int.Parse(match.Groups["index"].Value)]);

        return text.Trim();
    }

    private static string ReplaceCodeBlocks(string html, Func<string, string> replace)
    {
        var output = new StringBuilder(html.Length);
        int position = 0;
        while (CodeBlockOpen().Match(html, position) is { Success: true } open)
        {
            int contentStart = open.Index + open.Length;
            int close = FindClosingDiv(html, contentStart, out int closeLength);
            if (close < 0)
                break;

            output.Append(html, position, open.Index - position);
            output.Append(replace(CodeBlockText(html[contentStart..close])));
            position = close + closeLength;
        }

        output.Append(html, position, html.Length - position);
        return output.ToString();
    }

    private static int FindClosingDiv(string html, int from, out int closeLength)
    {
        int depth = 1;
        for (Match tag = DivTag().Match(html, from); tag.Success; tag = tag.NextMatch())
        {
            depth += tag.Groups["close"].Success ? -1 : 1;
            if (depth == 0)
            {
                closeLength = tag.Length;
                return tag.Index;
            }
        }

        closeLength = 0;
        return -1;
    }

    /// <summary>Zola's highlighter writes one <c>&lt;div&gt;</c> per line and a bare <c>&lt;br&gt;</c> per blank line.</summary>
    private static string CodeBlockText(string innerHtml)
    {
        string text = innerHtml.Replace("\r", "").Replace("\n", "");
        text = DivClose().Replace(text, "\n");
        text = LineBreak().Replace(text, "\n");
        text = Decode(StripTags(text));
        return string.Join('\n', text.Split('\n').Select(line => line.TrimEnd())).Trim('\n');
    }

    private static string? InnerHtml(string html, string tag)
    {
        Match open = Regex.Match(html, $@"<{tag}(\s[^>]*)?>", RegexOptions.IgnoreCase);
        if (!open.Success)
            return null;

        int close = html.LastIndexOf($"</{tag}>", StringComparison.OrdinalIgnoreCase);
        int start = open.Index + open.Length;
        return close > start ? html[start..close] : null;
    }

    private static string InlineText(string html) => Whitespace().Replace(Decode(StripTags(html)), " ").Trim();

    private static string StripTags(string html) => Tag().Replace(html, "");

    private static string Decode(string text) => WebUtility.HtmlDecode(text).Replace(' ', ' ');

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex Comment();

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<a\b[^>]*class=""zola-anchor""[^>]*>.*?</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex HeadingAnchor();

    [GeneratedRegex(@"<div\b[^>]*class=""[^""]*\bcode_block\b[^""]*""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex CodeBlockOpen();

    [GeneratedRegex(@"<(?<close>/)?div\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex DivTag();

    [GeneratedRegex(@"</div\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex DivClose();

    [GeneratedRegex(@"<pre\b[^>]*>(?<body>.*?)</pre>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Preformatted();

    [GeneratedRegex(@"<(?<tag>code|kbd)\b[^>]*>(?<body>.*?)</\k<tag>>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex InlineCode();

    [GeneratedRegex(@"<a\b[^>]*?\bhref=""(?<href>[^""]*)""[^>]*>(?<body>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Link();

    [GeneratedRegex(@"<img\b[^>]*?\balt=""(?<alt>[^""]+)""[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ImageWithAlt();

    [GeneratedRegex(@"<h(?<level>[1-6])\b[^>]*>(?<body>.*?)</h\k<level>>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Heading();

    [GeneratedRegex(@"<title>(?<body>.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex TitleTag();

    [GeneratedRegex(@"</?(strong|b)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Bold();

    [GeneratedRegex(@"</?(em|i)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Italic();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemOpen();

    [GeneratedRegex(@"<tr\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex TableRowOpen();

    [GeneratedRegex(@"</t[dh]\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex TableCellClose();

    [GeneratedRegex(@"<br\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<hr\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex Rule();

    [GeneratedRegex(@"<blockquote\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockquoteOpen();

    [GeneratedRegex(@"</blockquote\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockquoteClose();

    [GeneratedRegex(@"</?p\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ParagraphBoundary();

    [GeneratedRegex(@"</?(div|section|header|footer|nav|ul|ol|table|thead|tbody|dl|dt|dd|figure)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockBoundary();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t\r\n\f\v]+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ExtraBlankLines();

    [GeneratedRegex("(?<body>.*?)", RegexOptions.Singleline)]
    private static partial Regex Quote();

    [GeneratedRegex("(?<index>[0-9]+)")]
    private static partial Regex Placeholder();
}
