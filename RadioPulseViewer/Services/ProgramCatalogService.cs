using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RadioPulseViewer.Models;

namespace RadioPulseViewer.Services;

public sealed class ProgramCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ProgramCatalog Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data", "programs.json");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("番組データが見つかりません。", path);
        }

        string json = File.ReadAllText(path);
        ProgramCatalog? catalog = JsonSerializer.Deserialize<ProgramCatalog>(json, JsonOptions);
        if (catalog is null)
        {
            throw new InvalidDataException("番組データを読み込めませんでした。");
        }

        Validate(catalog);
        return catalog;
    }

    private static void Validate(ProgramCatalog catalog)
    {
        HashSet<string> stationIds = catalog.Stations
            .Select(station => station.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (stationIds.Count == 0)
        {
            throw new InvalidDataException("放送局データがありません。");
        }

        ProgramInfo? invalidProgram = catalog.Programs.FirstOrDefault(program =>
            string.IsNullOrWhiteSpace(program.Title) ||
            !stationIds.Contains(program.StationId));

        if (invalidProgram is not null)
        {
            throw new InvalidDataException($"番組データの放送局または番組名が不正です: {invalidProgram.Title}");
        }
    }
}
