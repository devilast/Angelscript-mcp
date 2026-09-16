using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;

namespace AngelscriptApiMcp.Guide;

/// <summary>
/// The hand-written Angelscript guide, downloaded from the guide site on first use and cached on disk.
/// </summary>
/// <remarks>
/// Only the guide pages listed in the site's sitemap are read. The site's <c>/api/</c> reference is
/// deliberately ignored: it was last generated in 2022, before UE5, so API lookups use the local
/// engine dump instead.
/// </remarks>
public sealed partial class GuideStore(ServerOptions options, HttpClient http, ILogger<GuideStore> logger)
{
    private const string CacheFileName = "guide.json";
    private const int MinimumPageLength = 80;

    /// <summary>Zola serves this stub for sections without a template (e.g. /scripting/ itself).</summary>
    private const string ZolaPlaceholderText = "couldn't find a template to render";
    private static readonly TimeSpan RetryDelayAfterFailure = TimeSpan.FromHours(1);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private GuideSnapshot? _snapshot;
    private DateTimeOffset _nextRefreshAttempt = DateTimeOffset.MinValue;

    private string CachePath => Path.Combine(options.GuideCacheDir, CacheFileName);

    [GeneratedRegex(@"<loc>\s*(?<url>[^<]+?)\s*</loc>", RegexOptions.IgnoreCase)]
    private static partial Regex SitemapLocation();

    public async Task<GuideSnapshot> GetAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        if (!forceRefresh && _snapshot is { } current && !NeedsRefresh(current))
            return current;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            GuideSnapshot? cached = _snapshot ?? ReadCache();
            if (!forceRefresh && cached is not null && !NeedsRefresh(cached))
                return _snapshot = cached;

            if (options.Offline)
            {
                return cached is not null
                    ? _snapshot = cached
                    : throw new McpException($"The guide is not cached at '{CachePath}' and the server runs with --offline.");
            }

            try
            {
                GuideSnapshot fresh = await FetchAsync(cancellationToken);
                WriteCache(fresh);
                _nextRefreshAttempt = DateTimeOffset.MinValue;
                return _snapshot = fresh;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or McpException)
            {
                _nextRefreshAttempt = DateTimeOffset.UtcNow + RetryDelayAfterFailure;
                if (cached is null)
                    throw new McpException($"Could not download the Angelscript guide from {options.GuideBaseUrl}: {ex.Message}");

                logger.LogWarning(ex, "Guide refresh failed; using the copy cached at {FetchedAt}", cached.FetchedAt);
                return _snapshot = cached with { RefreshError = ex.Message };
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public string DescribeState()
    {
        var lines = new List<string> { $"Source: {options.GuideBaseUrl}", $"Cache: {CachePath}" };
        GuideSnapshot? snapshot = _snapshot ?? ReadCache();
        if (snapshot is null)
        {
            lines.Add(options.Offline
                ? "Not cached, and --offline is set, so guide tools will fail."
                : "Not downloaded yet; it is fetched on the first guide tool call.");
            return string.Join('\n', lines);
        }

        string age = IsExpired(snapshot) ? " (expired; refreshed on next use)" : "";
        lines.Add($"{snapshot.Pages.Count} pages, downloaded {snapshot.FetchedAt:yyyy-MM-dd HH:mm} UTC{age}");
        if (snapshot.RefreshError is { } error)
            lines.Add("Last refresh failed: " + error);
        return string.Join('\n', lines);
    }

    /// <summary>False for empty section index pages, which have no prose of their own.</summary>
    public static bool IsContentPage(string markdown) =>
        markdown.Length >= MinimumPageLength && !markdown.Contains(ZolaPlaceholderText, StringComparison.OrdinalIgnoreCase);

    public static string SlugFor(Uri url)
    {
        string slug = Uri.UnescapeDataString(url.AbsolutePath).Trim('/');
        return slug.Length == 0 ? "index" : slug;
    }

    private bool IsExpired(GuideSnapshot snapshot) =>
        DateTimeOffset.UtcNow - snapshot.FetchedAt > options.GuideMaxAge
        || !string.Equals(snapshot.SourceUrl, options.GuideBaseUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase);

    /// <summary>Expired, and not inside the back-off window after a failed download.</summary>
    private bool NeedsRefresh(GuideSnapshot snapshot) => IsExpired(snapshot) && DateTimeOffset.UtcNow >= _nextRefreshAttempt;

    private async Task<GuideSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        Uri baseUrl = options.GuideBaseUrl;
        string sitemap = await http.GetStringAsync(new Uri(baseUrl, "sitemap.xml"), cancellationToken);

        // Rebuild every URL on the configured base so the scheme and host always match it.
        List<Uri> pageUrls = SitemapLocation().Matches(sitemap)
            .Select(match => Uri.TryCreate(WebUtility.HtmlDecode(match.Groups["url"].Value), UriKind.Absolute, out Uri? url) ? url : null)
            .OfType<Uri>()
            .Where(url => url.Host.Equals(baseUrl.Host, StringComparison.OrdinalIgnoreCase))
            .Select(url => new Uri(baseUrl, url.PathAndQuery))
            .DistinctBy(url => url.AbsoluteUri)
            .ToList();

        var pages = new List<GuidePage>();
        foreach (Uri pageUrl in pageUrls)
        {
            try
            {
                string html = await http.GetStringAsync(pageUrl, cancellationToken);
                (string title, string markdown) = HtmlToMarkdown.ConvertPage(html, pageUrl);
                if (!IsContentPage(markdown))
                    continue;

                string slug = SlugFor(pageUrl);
                pages.Add(new GuidePage(slug, pageUrl.AbsoluteUri, title.Length > 0 ? title : slug, markdown));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
            {
                logger.LogWarning(ex, "Skipping guide page {Url}", pageUrl);
            }
        }

        if (pages.Count == 0)
            throw new McpException($"No guide pages could be read from {baseUrl}.");

        logger.LogInformation("Downloaded {PageCount} guide pages from {BaseUrl}", pages.Count, baseUrl);
        return new GuideSnapshot(baseUrl.AbsoluteUri, DateTimeOffset.UtcNow, pages.OrderBy(page => page.Slug, StringComparer.Ordinal).ToList());
    }

    private GuideSnapshot? ReadCache()
    {
        try
        {
            if (!File.Exists(CachePath))
                return null;

            using FileStream stream = File.OpenRead(CachePath);
            GuideSnapshot? snapshot = JsonSerializer.Deserialize<GuideSnapshot>(stream);
            return snapshot is { Pages.Count: > 0 } ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(ex, "Ignoring unreadable guide cache {Path}", CachePath);
            return null;
        }
    }

    private void WriteCache(GuideSnapshot snapshot)
    {
        try
        {
            Directory.CreateDirectory(options.GuideCacheDir);
            string temporary = CachePath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            File.Move(temporary, CachePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not write the guide cache to {Path}; it will be downloaded again next start", CachePath);
        }
    }
}
