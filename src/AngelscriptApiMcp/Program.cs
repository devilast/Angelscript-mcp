using AngelscriptApiMcp;
using AngelscriptApiMcp.Api;
using AngelscriptApiMcp.Guide;
using AngelscriptApiMcp.Scripts;
using AngelscriptApiMcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

if (args.Any(arg => arg is "--help" or "-h" or "/?"))
{
    Console.Error.WriteLine(ServerOptions.Usage);
    return 0;
}

ServerOptions options;
try
{
    options = ServerOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    Console.Error.WriteLine(ServerOptions.Usage);
    return 2;
}

HostApplicationBuilder builder = Host.CreateApplicationBuilder();

// stdout carries the MCP protocol; every log line has to go to stderr instead.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(options);
builder.Services.AddSingleton(_ =>
{
    var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    http.DefaultRequestHeaders.UserAgent.ParseAdd($"angelscript-api-mcp/{ServerOptions.Version}");
    return http;
});
builder.Services.AddSingleton<ScriptIndex>();
builder.Services.AddSingleton<ApiIndexProvider>();
builder.Services.AddSingleton<GuideStore>();

builder.Services
    .AddMcpServer(server =>
    {
        server.ServerInfo = new Implementation { Name = "angelscript-api-mcp", Version = ServerOptions.Version };
        server.ServerInstructions = options.Instructions;
    })
    .WithStdioServerTransport()
    .WithTools<ApiTools>()
    .WithTools<GuideTools>()
    .WithTools<StatusTools>();

await builder.Build().RunAsync();
return 0;
