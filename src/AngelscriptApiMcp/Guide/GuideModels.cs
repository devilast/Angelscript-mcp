using System.Text.Json.Serialization;

namespace AngelscriptApiMcp.Guide;

/// <param name="Slug">URL path without slashes, e.g. <c>scripting/delegates</c>; <c>index</c> for the home page.</param>
public sealed record GuidePage(string Slug, string Url, string Title, string Markdown);

public sealed record GuideSnapshot(string SourceUrl, DateTimeOffset FetchedAt, IReadOnlyList<GuidePage> Pages)
{
    /// <summary>Set when a refresh failed and this older copy is being served instead.</summary>
    [JsonIgnore]
    public string? RefreshError { get; init; }
}
