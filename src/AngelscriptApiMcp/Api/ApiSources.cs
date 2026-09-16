using System.Collections.Concurrent;

namespace AngelscriptApiMcp.Api;

/// <summary>Engine types parsed from a <c>-dump-as-doc</c> folder.</summary>
public sealed record DumpInfo(string Directory, IReadOnlyList<ApiType> Types, IReadOnlyList<string> LoadErrors, DateTime NewestFileUtc)
{
    public static DumpInfo Load(string directory, CancellationToken cancellationToken = default)
    {
        string[] files = System.IO.Directory.GetFiles(directory, "*.hpp");
        var types = new ConcurrentBag<ApiType>();
        var errors = new ConcurrentBag<string>();

        Parallel.ForEach(files, new ParallelOptions { CancellationToken = cancellationToken }, file =>
        {
            try
            {
                // The engine writes plain ASCII, or UTF-16 with a BOM when a tooltip needs it; ReadAllText detects both.
                types.Add(DumpParser.Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        });

        DateTime newest = files.Length == 0 ? default : files.Max(File.GetLastWriteTimeUtc);
        return new DumpInfo(
            directory,
            types.OrderBy(type => type.Name, StringComparer.Ordinal).ToList(),
            errors.Order(StringComparer.Ordinal).ToList(),
            newest);
    }
}

/// <summary>Types declared in the project's own <c>.as</c> files at one point in time.</summary>
/// <param name="Version">Increases whenever a script file is added, changed or removed.</param>
public sealed record ScriptSnapshot(
    long Version,
    IReadOnlyList<ApiType> Types,
    IReadOnlyList<string> Roots,
    int FileCount,
    IReadOnlyList<string> Warnings,
    DateTime NewestFileUtc);
