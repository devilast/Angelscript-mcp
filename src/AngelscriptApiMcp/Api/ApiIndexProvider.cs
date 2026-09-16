using System.Diagnostics;
using AngelscriptApiMcp.Scripts;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace AngelscriptApiMcp.Api;

/// <summary>
/// Builds the index that API tools query: the engine dump, loaded once on first use, combined with the
/// project scripts, which are re-checked on every call and re-indexed when a file changes.
/// </summary>
/// <remarks>
/// Loading lazily lets the server answer the MCP handshake immediately, and reports a missing dump
/// through tool results instead of a crash. Either source alone is enough to answer queries.
/// </remarks>
public sealed class ApiIndexProvider(ServerOptions options, ScriptIndex scripts, ILogger<ApiIndexProvider> logger)
{
    public const string DumpHint =
        "Generate the dump by starting the editor once with the -dump-as-doc switch " +
        "(for example: UnrealEditor-Cmd.exe MyGame.uproject -dump-as-doc -unattended -nosplash -nullrhi). " +
        "It writes <Project>/Docs/angelscript/generated/*.hpp after the scripts compile, then exits.";

    /// <summary>How long to wait before retrying a dump that failed to load, so it can be generated without a restart.</summary>
    private static readonly TimeSpan DumpRetryInterval = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DumpInfo? _dump;
    private string? _dumpError;
    private DateTime _lastDumpAttemptUtc = DateTime.MinValue;
    private ApiIndex? _index;
    private DumpInfo? _indexedDump;
    private long _indexedScriptVersion = -1;

    public async Task<ApiIndex> GetAsync(bool reload = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (reload || (_dump is null && DateTime.UtcNow - _lastDumpAttemptUtc >= DumpRetryInterval))
                await LoadDumpAsync(cancellationToken);

            ScriptSnapshot? snapshot = scripts.IsConfigured ? scripts.Refresh(force: reload) : null;
            long scriptVersion = snapshot?.Version ?? 0;

            if (_index is null || !ReferenceEquals(_indexedDump, _dump) || _indexedScriptVersion != scriptVersion)
            {
                IEnumerable<ApiType> types = (_dump?.Types ?? []).Concat(snapshot?.Types ?? []);
                _index = new ApiIndex(types, _dump, snapshot, _dumpError);
                _indexedDump = _dump;
                _indexedScriptVersion = scriptVersion;
            }

            if (_dump is null && (snapshot is null || snapshot.Types.Count == 0))
                throw new McpException(_dumpError ?? "No API data is available.");

            return _index;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Accepts the generated folder itself or a project root that contains it.</summary>
    public static string ResolveDumpDirectory(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
            throw new McpException($"No API dump is configured. Start the server with --dump-dir <path> or set {ServerOptions.DumpDirEnv}. {DumpHint}");

        string directory = Path.GetFullPath(configured);
        if (!Directory.Exists(directory))
            throw new McpException($"The API dump folder '{directory}' does not exist. {DumpHint}");
        if (Directory.EnumerateFiles(directory, "*.hpp").Any())
            return directory;

        string nested = Path.Combine(directory, "Docs", "angelscript", "generated");
        if (Directory.Exists(nested) && Directory.EnumerateFiles(nested, "*.hpp").Any())
            return nested;

        throw new McpException($"No .hpp files found in '{directory}'. {DumpHint}");
    }

    private async Task LoadDumpAsync(CancellationToken cancellationToken)
    {
        _lastDumpAttemptUtc = DateTime.UtcNow;
        try
        {
            string directory = ResolveDumpDirectory(options.DumpDir);
            var stopwatch = Stopwatch.StartNew();
            _dump = await Task.Run(() => DumpInfo.Load(directory, cancellationToken), cancellationToken);
            _dumpError = null;
            logger.LogInformation("Loaded {TypeCount} engine types from {Directory} in {Elapsed} ms",
                _dump.Types.Count, directory, stopwatch.ElapsedMilliseconds);
        }
        catch (McpException ex)
        {
            _dump = null;
            _dumpError = ex.Message;
        }
    }
}
