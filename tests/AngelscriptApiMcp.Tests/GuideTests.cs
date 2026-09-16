using AngelscriptApiMcp.Guide;

namespace AngelscriptApiMcp.Tests;

public class HtmlToMarkdownTests
{
    private static readonly Uri PageUrl = new("https://angelscript.hazelight.se/scripting/delegates/");

    [Fact]
    public void ConvertsHighlightedCodeBlocks()
    {
        // Same markup shape as the guide site: one <div> per code line, a bare <br> per blank line.
        const string html = """
            <html><body><main><article>
            <h1 id="sample">Sample Page<a class="zola-anchor" href="#sample" aria-label="Anchor link for: sample">🔗</a></h1>
            <p>First line of the intro.<br />
            Second line:</p>
            <div class="code_block" style="white-space: pre;"><div><span style="color: #569cd6;">class</span><span> </span><span>AMyActor</span><span> : </span><span>AActor</span></div><div><span>{</span></div><div><span>&#160; &#160; </span><span>int</span><span> Counter;</span></div><br><div><span>}</span></div></div>
            <p>Closing text.</p>
            </article></main></body></html>
            """;

        (string title, string markdown) = HtmlToMarkdown.ConvertPage(html, PageUrl);

        Assert.Equal("Sample Page", title);
        Assert.Equal(
            "# Sample Page\n\nFirst line of the intro.\nSecond line:\n\n" +
            "```angelscript\nclass AMyActor : AActor\n{\n    int Counter;\n\n}\n```\n\nClosing text.",
            markdown);
    }

    [Fact]
    public void KeepsInlineCodeLinksAndLists()
    {
        const string html = """
            <article><p>Use <code>TArray&lt;AActor&gt;</code> with <a href="/scripting/mixin-methods/">mixins</a>.</p>
            <ul><li>First <strong>bold</strong></li><li>Second</li></ul></article>
            """;

        (_, string markdown) = HtmlToMarkdown.ConvertPage(html, PageUrl);

        Assert.Equal("Use `TArray<AActor>` with [mixins](https://angelscript.hazelight.se/scripting/mixin-methods/).\n\n- First **bold**\n- Second", markdown);
    }

    [Fact]
    public void PrefixesBlockquotes()
    {
        (_, string markdown) = HtmlToMarkdown.ConvertPage("<article><blockquote><p>Note one</p><p>Note two</p></blockquote><p>After</p></article>", PageUrl);

        Assert.Equal("> Note one\n>\n> Note two\n\nAfter", markdown);
    }
}

public class GuideSearchTests
{
    private static readonly GuidePage[] Pages =
    [
        new("scripting/delegates", "https://angelscript.hazelight.se/scripting/delegates/", "Delegates",
            "# Delegates\n\nDeclare a delegate type.\n\n## Events\n\nEvents can be bound with AddUFunction.\n\n```angelscript\n# not a heading\n```"),
        new("scripting/format-strings", "https://angelscript.hazelight.se/scripting/format-strings/", "Format Strings",
            "# Format Strings\n\nUse f\"{Value}\" to format values into text."),
    ];

    [Fact]
    public void SplitsSectionsOnHeadingsOutsideCode()
    {
        IReadOnlyList<GuideSection> sections = GuideSearch.SplitSections(Pages[0]);

        Assert.Equal(new[] { "Delegates", "Events" }, sections.Select(section => section.Heading));
        Assert.Contains("# not a heading", sections[1].Text);
    }

    [Fact]
    public void RanksHeadingMatchesFirst()
    {
        Assert.Equal("Events", GuideSearch.Search(Pages, "events bound", 5)[0].Section.Heading);
    }

    [Fact]
    public void FallsBackToAnyWordWhenNoSectionHasAll()
    {
        Assert.NotEmpty(GuideSearch.Search(Pages, "format nonexistentword", 5));
    }

    [Fact]
    public void SkipsZolaPlaceholderSectionPages()
    {
        const string placeholder = "# Welcome to Zola!\n\nYou're seeing this page because we couldn't find a template to render.\n\n" +
                                   "To modify this page, create a **section.html** file in the templates directory.";

        Assert.False(GuideStore.IsContentPage(placeholder));
        Assert.True(GuideStore.IsContentPage(Pages[0].Markdown));
    }

    [Theory]
    [InlineData("scripting/delegates")]
    [InlineData("delegates")]
    [InlineData("Delegates")]
    [InlineData("https://angelscript.hazelight.se/scripting/delegates/")]
    public void FindsPagesBySlugTitleOrUrl(string page)
    {
        Assert.Equal("scripting/delegates", GuideSearch.FindPage(Pages, page)?.Slug);
    }
}
