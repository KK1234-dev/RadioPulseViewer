using System.Text.Json.Serialization;

namespace RadioPulseViewer.Models;

public sealed class ProgramCatalog
{
    [JsonPropertyName("lastReviewed")]
    public string LastReviewed { get; set; } = string.Empty;

    [JsonPropertyName("dataNotice")]
    public string DataNotice { get; set; } = string.Empty;

    [JsonPropertyName("stations")]
    public List<StationInfo> Stations { get; set; } = [];

    [JsonPropertyName("programs")]
    public List<ProgramInfo> Programs { get; set; } = [];
}
