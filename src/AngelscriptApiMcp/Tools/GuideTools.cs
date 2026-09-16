using System.ComponentModel;
using System.Text;
using AngelscriptApiMcp.Api;
using AngelscriptApiMcp.Guide;
using ModelContextProtocol.Server;

namespace AngelscriptApiMcp.Tools;

[McpServerToolType]
public sealed class GuideTools
{
    [McpServerTool(Name = "search_guide", Title = "Search the Angelscript guide", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Search the Unreal Engine Angelscript language guide (angelscript.hazelight.se) for syntax and how-to topics: " +
                 "UCLASS/UPROPERTY/UFUNCTION specifiers, default components, delegates and events, mixin methods, format strings, " +
                 "networking, subsystems, script tests and C++ bindings. Returns matching sections with snippets; " +
                 "read a whole page with read_guide_page.")]
    public static async Task<string> SearchGuide(
        GuideStore guide,
        [Description("Words to look for, e.g. 'delegate bind' or 'replicated property'.")] string query,
        [Description("Maximum number of sections, 1-20.")] int limit = 6,
        CancellationToken cancellationToken = default)
    {
        GuideSnapshot snapshot = await guide.GetAsync(cancellationToken: cancellationToken);
        IReadOnlyList<GuideHit> hits = GuideSearch.Search(snapshot.Pages, query, Math.Clamp(limit, 1, 20));
        if (hits.Count == 0)
            return $"No guide sections match \"{query}\". Available pages:\n{ListPages(snapshot)}";

        var output = new StringBuilder();
        output.AppendLine($"Guide sections matching \"{query}\":");
        int number = 0;
        foreach (GuideHit hit in hits)
        {
            GuideSection section = hit.Section;
            string location = section.Heading == section.Page.Title ? section.Page.Title : $"{section.Page.Title} › {section.Heading}";
            output.AppendLine().AppendLine($"{++number}. {location}  [page: {section.Page.Slug}]");
            output.AppendLine("   " + hit.Snippet);
        }

        return output.ToString();
    }

    [McpServerTool(Name = "read_guide_page", Title = "Read an Angelscript guide page", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("Read one page of the Angelscript language guide as Markdown, including its code examples. " +
                 "Accepts a slug from search_guide or list_guide_pages ('scripting/delegates', or just 'delegates'), a page title, or the page URL.")]
    public static async Task<string> ReadGuidePage(
        GuideStore guide,
        [Description("Page slug, title or URL.")] string page,
        CancellationToken cancellationToken = default)
    {
        GuideSnapshot snapshot = await guide.GetAsync(cancellationToken: cancellationToken);
        GuidePage? found = GuideSearch.FindPage(snapshot.Pages, page);
        if (found is null)
            return $"No guide page matches \"{page}\". Available pages:\n{ListPages(snapshot)}";

        return ApiText.Truncate($"Source: {found.Url}\n\n{found.Markdown}", ApiFormatter.MaxOutputChars, "The rest of the page is on the website.");
    }

    [McpServerTool(Name = "list_guide_pages", Title = "List Angelscript guide pages", ReadOnly = true, Idempotent = true, OpenWorld = true)]
    [Description("List every page of the Angelscript language guide with its slug and title.")]
    public static async Task<string> ListGuidePages(GuideStore guide, CancellationToken cancellationToken = default)
    {
        GuideSnapshot snapshot = await guide.GetAsync(cancellationToken: cancellationToken);
        return $"Angelscript guide, {snapshot.Pages.Count} pages from {snapshot.SourceUrl}:\n{ListPages(snapshot)}";
    }

    private static string ListPages(GuideSnapshot snapshot) =>
        string.Join('\n', snapshot.Pages.Select(page => $"- {page.Slug} — {page.Title}"));
}
