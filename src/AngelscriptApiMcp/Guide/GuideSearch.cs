using System.Text;
using System.Text.RegularExpressions;

namespace AngelscriptApiMcp.Guide;

public sealed record GuideSection(GuidePage Page, string Heading, string Text);

public sealed record GuideHit(GuideSection Section, int Score, string Snippet);

/// <summary>Keyword search over guide pages, one Markdown heading section at a time.</summary>
public static partial class GuideSearch
{
    private const int SnippetLength = 320;

    [GeneratedRegex(@"[^\p{L}\p{N}_]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static IReadOnlyList<GuideHit> Search(IEnumerable<GuidePage> pages, string query, int limit)
    {
        string[] tokens = NonWord().Split(query.ToLowerInvariant())
            .Where(token => token.Length >= 2)
            .Distinct()
            .ToArray();
        if (tokens.Length == 0)
            return [];

        List<GuideSection> sections = pages.SelectMany(SplitSections).ToList();
        List<GuideHit> hits = Rank(sections, tokens, requireAll: true);
        if (hits.Count == 0 && tokens.Length > 1)
            hits = Rank(sections, tokens, requireAll: false);

        return hits.OrderByDescending(hit => hit.Score).Take(limit).ToList();
    }

    public static IReadOnlyList<GuideSection> SplitSections(GuidePage page)
    {
        var sections = new List<GuideSection>();
        var text = new StringBuilder();
        string heading = page.Title;
        bool inCodeFence = false;

        foreach (string line in page.Markdown.Split('\n'))
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
                inCodeFence = !inCodeFence;

            if (!inCodeFence && line.StartsWith('#'))
            {
                Flush();
                heading = line.TrimStart('#').Trim();
                continue;
            }

            text.Append(line).Append('\n');
        }

        Flush();
        return sections;

        void Flush()
        {
            string body = text.ToString().Trim();
            if (body.Length > 0)
                sections.Add(new GuideSection(page, heading, body));
            text.Clear();
        }
    }

    /// <summary>Finds a page by slug, trailing slug segment (<c>delegates</c>), title or full URL.</summary>
    public static GuidePage? FindPage(IReadOnlyList<GuidePage> pages, string page)
    {
        string title = page.Trim();
        string slug = Uri.TryCreate(title, UriKind.Absolute, out Uri? url) && url.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? GuideStore.SlugFor(url)
            : title.Trim('/');
        if (slug.Length == 0)
            slug = "index";

        return pages.FirstOrDefault(candidate => candidate.Slug.Equals(slug, StringComparison.OrdinalIgnoreCase))
            ?? pages.FirstOrDefault(candidate => candidate.Slug.EndsWith("/" + slug, StringComparison.OrdinalIgnoreCase))
            ?? pages.FirstOrDefault(candidate => candidate.Title.Equals(title, StringComparison.OrdinalIgnoreCase));
    }

    private static List<GuideHit> Rank(List<GuideSection> sections, string[] tokens, bool requireAll)
    {
        var hits = new List<GuideHit>();
        foreach (GuideSection section in sections)
        {
            int score = 0;
            int matchedTokens = 0;
            foreach (string token in tokens)
            {
                int occurrences = CountOccurrences(section.Text, token, cap: 10);
                bool inHeading = section.Heading.Contains(token, StringComparison.OrdinalIgnoreCase);
                bool inTitle = section.Page.Title.Contains(token, StringComparison.OrdinalIgnoreCase);
                if (occurrences == 0 && !inHeading && !inTitle)
                    continue;

                matchedTokens++;
                score += occurrences + (inHeading ? 8 : 0) + (inTitle ? 4 : 0);
            }

            if (matchedTokens == 0 || (requireAll && matchedTokens < tokens.Length))
                continue;

            hits.Add(new GuideHit(section, score + matchedTokens * 5, Snippet(section.Text, tokens)));
        }

        return hits;
    }

    private static int CountOccurrences(string text, string token, int cap)
    {
        int count = 0;
        for (int index = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
             index >= 0 && count < cap;
             index = text.IndexOf(token, index + token.Length, StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    private static string Snippet(string text, string[] tokens)
    {
        int first = tokens
            .Select(token => text.IndexOf(token, StringComparison.OrdinalIgnoreCase))
            .Where(index => index >= 0)
            .DefaultIfEmpty(0)
            .Min();

        int start = Math.Max(0, first - SnippetLength / 3);
        int end = Math.Min(text.Length, start + SnippetLength);
        string snippet = Whitespace().Replace(text[start..end], " ").Trim();
        return (start > 0 ? "…" : "") + snippet + (end < text.Length ? "…" : "");
    }
}
