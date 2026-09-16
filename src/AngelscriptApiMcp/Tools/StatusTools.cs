using System.ComponentModel;
using System.Text;
using AngelscriptApiMcp.Api;
using AngelscriptApiMcp.Guide;
using AngelscriptApiMcp.Scripts;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace AngelscriptApiMcp.Tools;

[McpServerToolType]
public sealed class StatusTools
{
    [McpServerTool(Name = "status", Title = "Angelscript reference status", ReadOnly = true, Idempotent = false, OpenWorld = true)]
    [Description("Show where the API dump, the project scripts and the guide are loaded from, how much they contain and how old they are. " +
                 "Use reloadApi after regenerating the dump with -dump-as-doc, and refreshGuide to download the guide again now.")]
    public static async Task<string> Status(
        ApiIndexProvider api,
        ScriptIndex scripts,
        GuideStore guide,
        [Description("Re-read the API dump and all script files from disk.")] bool reloadApi = false,
        [Description("Download the guide again instead of waiting for the cache to expire.")] bool refreshGuide = false,
        CancellationToken cancellationToken = default)
    {
        var output = new StringBuilder();
        output.AppendLine($"angelscript-api-mcp {ServerOptions.Version}").AppendLine();

        try
        {
            ApiIndex index = await api.GetAsync(reloadApi, cancellationToken);
            output.Append(ApiFormatter.FormatStatus(index, scripts.IsConfigured));
        }
        catch (McpException ex)
        {
            output.AppendLine("## API").AppendLine("Not available: " + ex.Message);
        }

        output.AppendLine().AppendLine("## Guide");
        if (refreshGuide)
        {
            try
            {
                await guide.GetAsync(forceRefresh: true, cancellationToken);
            }
            catch (McpException ex)
            {
                output.AppendLine("Refresh failed: " + ex.Message);
            }
        }

        output.AppendLine(guide.DescribeState());
        return output.ToString();
    }
}
