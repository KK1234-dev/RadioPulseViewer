namespace RadioPulseViewer.Models;

public sealed class PublicGraphRecord
{
    public required string Source { get; init; }

    public required string Query { get; init; }

    public required string RangeLabel { get; init; }

    public required DateTimeOffset RecordedAt { get; init; }

    public long TotalPostCount { get; init; }

    public required IReadOnlyList<PublicGraphPoint> Points { get; init; }

    public string Note { get; init; } = string.Empty;
}

public sealed class PublicGraphPoint
{
    public int Order { get; init; }

    public required string Label { get; init; }

    public long PostCount { get; init; }
}
