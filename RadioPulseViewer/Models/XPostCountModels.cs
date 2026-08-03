using System.Text.Json.Serialization;

namespace RadioPulseViewer.Models;

public sealed class XPostCountResult
{
    public required string Query { get; init; }

    public required string EffectiveQuery { get; init; }

    public required DateTimeOffset RetrievedAt { get; init; }

    public required DateTimeOffset RangeStart { get; init; }

    public required DateTimeOffset RangeEnd { get; init; }

    public required IReadOnlyList<XPostCountBucket> Buckets { get; init; }

    public long TotalPostCount { get; init; }
}

public sealed class XPostCountBucket
{
    public DateTimeOffset Start { get; init; }

    public DateTimeOffset End { get; init; }

    public long PostCount { get; init; }
}

internal sealed class XPostCountsApiResponse
{
    [JsonPropertyName("data")]
    public List<XPostCountsApiBucket>? Data { get; init; }

    [JsonPropertyName("meta")]
    public XPostCountsApiMeta? Meta { get; init; }

    [JsonPropertyName("errors")]
    public List<XPostCountsApiError>? Errors { get; init; }
}

internal sealed class XPostCountsApiBucket
{
    [JsonPropertyName("start")]
    public DateTimeOffset Start { get; init; }

    [JsonPropertyName("end")]
    public DateTimeOffset End { get; init; }

    [JsonPropertyName("tweet_count")]
    public long PostCount { get; init; }
}

internal sealed class XPostCountsApiMeta
{
    [JsonPropertyName("total_tweet_count")]
    public long TotalPostCount { get; init; }
}

internal sealed class XPostCountsApiError
{
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("status")]
    public int? Status { get; init; }
}
