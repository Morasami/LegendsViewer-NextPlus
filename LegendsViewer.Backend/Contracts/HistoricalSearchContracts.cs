namespace LegendsViewer.Backend.Contracts;

/// <summary>Stable category names used by the search index and API filters.</summary>
public enum HistoricalSearchCategory
{
    Figure,
    Entity,
    Site,
    Place,
    Artifact,
    War,
    Battle,
    EventCollection,
    Event,
    Creature
}

public sealed class HistoricalSearchResultDto
{
    /// <summary>Stable within one world, for example "figure:123" or "event:456".</summary>
    public string Key { get; set; } = string.Empty;
    public HistoricalSearchCategory Category { get; set; }
    public string CategoryLabel { get; set; } = string.Empty;
    public int? Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? Year { get; set; }
    public string? DateLabel { get; set; }
    public string? Url { get; set; }
    public bool Navigable { get; set; }
    public string MatchKind { get; set; } = string.Empty;
    public int Rank { get; set; }
}

public sealed class HistoricalSearchResponse
{
    public string Query { get; set; } = string.Empty;
    public bool WorldLoaded { get; set; }
    public long Generation { get; set; }
    public string? WorldName { get; set; }
    public string? WorldAlternativeName { get; set; }
    public int? CurrentYear { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool Truncated { get; set; }
    public int IndexBuildMilliseconds { get; set; }
    public int IndexedRecordCount { get; set; }
    public List<HistoricalSearchResultDto> Items { get; set; } = [];
    public Dictionary<string, int> AvailableCategoryCounts { get; set; } = [];
    public Dictionary<string, int> MatchedCategoryCounts { get; set; } = [];
    public List<string> UnavailableCategories { get; set; } = [];
    public string? Message { get; set; }
    public string? Warning { get; set; }
}

public sealed class HistoricalSearchMetadataDto
{
    public bool WorldLoaded { get; set; }
    public long Generation { get; set; }
    public string? WorldName { get; set; }
    public string? WorldAlternativeName { get; set; }
    public int? CurrentYear { get; set; }
    public int IndexBuildMilliseconds { get; set; }
    public int IndexedRecordCount { get; set; }
    public long EstimatedIndexBytes { get; set; }
    public Dictionary<string, int> CategoryCounts { get; set; } = [];
    public List<string> UnavailableCategories { get; set; } = [];
}
