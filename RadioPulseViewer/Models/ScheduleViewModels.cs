using System.Windows;

namespace RadioPulseViewer.Models;

public sealed class ScheduledProgram
{
    public required ProgramInfo Program { get; init; }
    public required StationInfo Station { get; init; }
    public required DateTime BroadcastDate { get; init; }
    public required string TimeText { get; init; }
    public bool IsOnAir { get; init; }
    public Visibility OnAirVisibility => IsOnAir ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class DaySchedule
{
    public required DateTime Date { get; init; }
    public required string Header { get; init; }
    public required string SubHeader { get; init; }
    public List<ScheduledProgram> Programs { get; init; } = [];
}

public sealed class StationFilterItem
{
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    public override string ToString() => DisplayName;
}
