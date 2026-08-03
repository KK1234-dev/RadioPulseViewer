using System.Text.Json.Serialization;

namespace RadioPulseViewer.Models;

public sealed class StationInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("shortName")]
    public string ShortName { get; set; } = string.Empty;

    [JsonPropertyName("radikoUrl")]
    public string RadikoUrl { get; set; } = string.Empty;

    [JsonPropertyName("officialScheduleUrl")]
    public string OfficialScheduleUrl { get; set; } = string.Empty;

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(ShortName) ? Name : $"{ShortName}  {Name}";
}
