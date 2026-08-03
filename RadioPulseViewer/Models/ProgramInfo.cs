using System.Text.Json.Serialization;

namespace RadioPulseViewer.Models;

public sealed class ProgramInfo
{
    private string _start = "00:00";
    private string _end = "00:00";
    private int _startMinutes;
    private int _endMinutes;

    [JsonPropertyName("stationId")]
    public string StationId { get; set; } = string.Empty;

    [JsonPropertyName("day")]
    public DayOfWeek Day { get; set; }

    [JsonPropertyName("start")]
    public string Start
    {
        get => _start;
        set
        {
            _start = value ?? "00:00";
            _startMinutes = ParseMinutes(_start);
        }
    }

    [JsonPropertyName("end")]
    public string End
    {
        get => _end;
        set
        {
            _end = value ?? "00:00";
            _endMinutes = ParseMinutes(_end);
        }
    }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("performers")]
    public string Performers { get; set; } = string.Empty;

    [JsonPropertyName("hashtag")]
    public string Hashtag { get; set; } = string.Empty;

    [JsonPropertyName("searchKeyword")]
    public string SearchKeyword { get; set; } = string.Empty;

    [JsonPropertyName("programUrl")]
    public string ProgramUrl { get; set; } = string.Empty;

    [JsonPropertyName("radikoUrl")]
    public string RadikoUrl { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// radiko番組表から取得した実際の放送日。
    /// nullの場合は従来どおりDayを毎週の曜日として扱います。
    /// </summary>
    [JsonIgnore]
    public DateTime? BroadcastDate { get; set; }

    [JsonIgnore]
    public int StartMinutes => _startMinutes;

    [JsonIgnore]
    public int EndMinutes => _endMinutes;

    [JsonIgnore]
    public string EffectiveSearchKeyword => !string.IsNullOrWhiteSpace(Hashtag)
        ? Hashtag
        : !string.IsNullOrWhiteSpace(SearchKeyword)
            ? SearchKeyword
            : Title;

    private static int ParseMinutes(string value)
    {
        string[] parts = value.Split(':', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], out int hours) ||
            !int.TryParse(parts[1], out int minutes))
        {
            return 0;
        }

        return (hours * 60) + minutes;
    }
}
