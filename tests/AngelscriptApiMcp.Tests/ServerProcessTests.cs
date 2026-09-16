using System.Text.Json;
using AngelscriptApiMcp.Guide;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AngelscriptApiMcp.Tests;

/// <summary>Starts the real server over stdio, as an MCP client would, against a sample dump and an offline guide cache.</summary>
public sealed class ServerFixture : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "angelscript-api-mcp-tests", Guid.NewGuid().ToString("N"));

    public McpClient Client { get; private set; } = null!;

    public string ScriptDirectory => Path.Combine(_root, "Script");

    public async Task InitializeAsync()
    {
        // Laid out like a game project, to exercise --dump-dir pointing at the project root.
        string dumpDirectory = Path.Combine(_root, "Docs", "angelscript", "generated");
        Directory.CreateDirectory(dumpDirectory);
        foreach ((string name, string text) in SampleApi.Files)
            await File.WriteAllTextAsync(Path.Combine(dumpDirectory, name + ".hpp"), text);

        string cacheDirectory = Path.Combine(_root, "guide-cache");
        Directory.CreateDirectory(cacheDirectory);
        var guide = new GuideSnapshot("https://angelscript.hazelight.se/", DateTimeOffset.UtcNow,
        [
            new GuidePage("scripting/delegates", "https://angelscript.hazelight.se/scripting/delegates/", "Delegates",
                "# Delegates\n\nBind a delegate with AddUFunction and a name literal."),
        ]);
        await File.WriteAllTextAsync(Path.Combine(cacheDirectory, "guide.json"), JsonSerializer.Serialize(guide));

        string pickupScript = Path.Combine(ScriptDirectory, "Pickups", "ExamplePickup.as");
        Directory.CreateDirectory(Path.GetDirectoryName(pickupScript)!);
        await File.WriteAllTextAsync(pickupScript, SampleScripts.Pickup);

        var transport = new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "angelscript-api-mcp",
            Command = "dotnet",
            Arguments =
            [
                FindServerAssembly(), "--dump-dir", _root, "--script-dir", ScriptDirectory,
                "--guide-cache-dir", cacheDirectory, "--offline",
            ],
        });
        Client = await McpClient.CreateAsync(transport);
    }

    public async Task DisposeAsync()
    {
        await Client.DisposeAsync();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Temp files; leftovers are harmless.
        }
    }

    private static string FindServerAssembly()
    {
        // tests/AngelscriptApiMcp.Tests/bin/<Configuration>/<tfm>/ -> src/AngelscriptApiMcp/bin/<Configuration>/<tfm>/
        var output = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
        string framework = output.Name;
        string configuration = output.Parent!.Name;
        DirectoryInfo repository = output.Parent!.Parent!.Parent!.Parent!.Parent!;
        string path = Path.Combine(repository.FullName, "src", "AngelscriptApiMcp", "bin", configuration, framework, "angelscript-api-mcp.dll");
        return File.Exists(path) ? path : throw new FileNotFoundException("Build the server project first.", path);
    }
}

public sealed class ServerProcessTests(ServerFixture server) : IClassFixture<ServerFixture>
{
    [Fact]
    public async Task ListsAllTools()
    {
        IList<McpClientTool> tools = await server.Client.ListToolsAsync();

        Assert.Equal(
            new[] { "get_member", "get_type", "list_guide_pages", "list_script_types", "read_guide_page", "search_api", "search_guide", "status" },
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task ScriptClassesShowTheirSourceAndEngineParents()
    {
        string text = await CallAsync("get_type", new() { ["name"] = "AExamplePickup", ["includeInherited"] = true });

        Assert.Contains("# AExamplePickup (script class)", text);
        Assert.Contains($"Declared in Script/Pickups/ExamplePickup.as:{SampleScripts.LineOf("class AExamplePickup")}", text);
        Assert.Contains("Inheritance: AExamplePickup → AActor → UObject", text);
        Assert.Contains("## Inherited from AActor", text);
    }

    [Fact]
    public async Task ListScriptTypesFiltersByBaseClass()
    {
        string text = await CallAsync("list_script_types", new() { ["baseClass"] = "Actor" });

        Assert.Contains("## Script/Pickups/ExamplePickup.as", text);
        Assert.Contains("class AExamplePickup : AActor", text);
        Assert.DoesNotContain("FExampleData", text);
    }

    [Fact]
    public async Task NewScriptFilesAreIndexedWithoutRestarting()
    {
        await File.WriteAllTextAsync(Path.Combine(server.ScriptDirectory, "ExampleLateAddition.as"), "class AExampleLateAddition : APawn {}");
        await CallAsync("status", new() { ["reloadApi"] = true });

        string text = await CallAsync("search_api", new() { ["query"] = "LateAddition", ["origin"] = "script" });

        Assert.Contains("script class  AExampleLateAddition : APawn", text);
    }

    [Fact]
    public async Task GetTypeReturnsInheritanceAndInheritedMembers()
    {
        string text = await CallAsync("get_type", new() { ["name"] = "Character", ["includeInherited"] = true });

        Assert.Contains("# ACharacter (type)", text);
        Assert.Contains("Inheritance: ACharacter → APawn → AActor → UObject", text);
        Assert.Contains("bool SetActorLocation(FVector NewLocation)", text);
    }

    [Fact]
    public async Task SearchApiFindsNamespaceFunctions()
    {
        string text = await CallAsync("search_api", new() { ["query"] = "LineTrace" });

        Assert.Contains("System::LineTraceSingle", text);
    }

    [Fact]
    public async Task GuideToolsWorkFromTheOfflineCache()
    {
        string text = await CallAsync("search_guide", new() { ["query"] = "AddUFunction" });

        Assert.Contains("[page: scripting/delegates]", text);
    }

    [Fact]
    public async Task StatusReportsTheDumpFolder()
    {
        string text = await CallAsync("status", new());

        Assert.Contains(Path.Combine("Docs", "angelscript", "generated"), text);
        Assert.Contains("Types: 8", text);
        Assert.Contains("## Project scripts", text);
    }

    [Fact]
    public async Task InvalidArgumentsComeBackAsToolErrors()
    {
        CallToolResult result = await server.Client.CallToolAsync("search_api", new Dictionary<string, object?> { ["query"] = "Actor", ["kind"] = "banana" });

        Assert.True(result.IsError);
    }

    private async Task<string> CallAsync(string tool, Dictionary<string, object?> arguments)
    {
        CallToolResult result = await server.Client.CallToolAsync(tool, arguments);
        string text = string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));
        Assert.False(result.IsError == true, text);
        return text;
    }
}
