using AngelscriptApiMcp.Api;
using Microsoft.Extensions.Logging;

namespace AngelscriptApiMcp.Scripts;

/// <summary>
/// Keeps the declarations of the project's <c>.as</c> files current. Scripts change constantly while
/// the editor hot-reloads them, so every refresh compares file timestamps and re-parses only files
/// that were added or changed.
/// </summary>
/// <remarks>Not thread-safe; <see cref="ApiIndexProvider"/> serialises access.</remarks>
public sealed class ScriptIndex(ServerOptions options, ILogger<ScriptIndex> logger)
{
    /// <summary>Scanning is cheap but not free; consecutive tool calls within this window share one scan.</summary>
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<string, ParsedFile> _files = new(StringComparer.OrdinalIgnoreCase);
    private ScriptSnapshot? _snapshot;
    private DateTime _lastScanUtc = DateTime.MinValue;
    private long _version;

    private sealed record ParsedFile(DateTime WriteTimeUtc, long Length, IReadOnlyList<ApiType> Types, string? Warning);

    public bool IsConfigured => options.ScriptDirs.Count > 0;

    public ScriptSnapshot Refresh(bool force = false)
    {
        if (!force && _snapshot is not null && DateTime.UtcNow - _lastScanUtc < ScanInterval)
            return _snapshot;

        _lastScanUtc = DateTime.UtcNow;
        bool changed = _snapshot is null;
        var roots = new List<string>();
        var folderWarnings = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string configured in options.ScriptDirs)
        {
            string root = Path.GetFullPath(configured);
            roots.Add(root);
            if (!Directory.Exists(root))
            {
                folderWarnings.Add($"Script folder '{root}' does not exist.");
                continue;
            }

            foreach (string file in Directory.EnumerateFiles(root, "*.as", SearchOption.AllDirectories))
            {
                if (!seen.Add(file))
                    continue; // listed under two overlapping roots

                var info = new FileInfo(file);
                if (_files.TryGetValue(file, out ParsedFile? cached) && cached.WriteTimeUtc == info.LastWriteTimeUtc && cached.Length == info.Length)
                    continue;

                _files[file] = ParseFile(root, file, info);
                changed = true;
            }
        }

        foreach (string removed in _files.Keys.Where(file => !seen.Contains(file)).ToList())
        {
            _files.Remove(removed);
            changed = true;
        }

        if (changed || !_snapshot!.Warnings.SequenceEqual(CollectWarnings(folderWarnings)))
        {
            _version++;
            _snapshot = new ScriptSnapshot(
                _version,
                _files.OrderBy(file => file.Key, StringComparer.OrdinalIgnoreCase).SelectMany(file => file.Value.Types).ToList(),
                roots,
                _files.Count,
                CollectWarnings(folderWarnings),
                _files.Count == 0 ? default : _files.Values.Max(file => file.WriteTimeUtc));
            logger.LogInformation("Indexed {TypeCount} script types from {FileCount} files (version {Version})",
                _snapshot.Types.Count, _snapshot.FileCount, _version);
        }

        return _snapshot;
    }

    private List<string> CollectWarnings(List<string> folderWarnings) =>
        folderWarnings.Concat(_files.Values.Select(file => file.Warning).OfType<string>()).ToList();

    private ParsedFile ParseFile(string root, string file, FileInfo info)
    {
        // Shown as "<root folder>/<relative path>", e.g. Script/Pickups/Pickup.as.
        string displayPath = Path.Combine(Path.GetFileName(root), Path.GetRelativePath(root, file)).Replace('\\', '/');
        try
        {
            IReadOnlyList<ApiType> types = ScriptParser.Parse(File.ReadAllText(file), displayPath);
            return new ParsedFile(info.LastWriteTimeUtc, info.Length, types, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Often the editor or IDE is mid-save; the changed timestamp triggers a retry on the next scan.
            return new ParsedFile(info.LastWriteTimeUtc, info.Length, [], $"{displayPath}: {ex.Message}");
        }
    }
}
