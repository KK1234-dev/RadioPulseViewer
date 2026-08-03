using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using RadioPulseViewer.Models;

namespace RadioPulseViewer.Services;

public sealed class RadikoScheduleService
{
    private const string AreaId = "JP13";
    private const int MaxConcurrentRequests = 3;
    private static readonly TimeSpan CurrentCacheLifetime = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan PastCacheLifetime = TimeSpan.FromHours(12);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhiteSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex HashtagRegex = new(
        @"(?<![\p{L}\p{N}_])#[\p{L}\p{N}_ー]+",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(200));

    private static readonly HttpClient HttpClient = CreateHttpClient();

    private readonly string _cacheDirectory;

    public RadikoScheduleService()
    {
        _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadioPulseViewer",
            "ScheduleCache");
    }

    public async Task<RadikoWeekScheduleResult> LoadWeekAsync(
        DateTime weekStart,
        IReadOnlyCollection<StationInfo> configuredStations,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        DateTime normalizedWeekStart = weekStart.Date;
        HashSet<string> stationIds = configuredStations
            .Select(station => station.Id)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await Task.Run(
                () => Directory.CreateDirectory(_cacheDirectory),
                cancellationToken)
            .ConfigureAwait(false);

        using SemaphoreSlim requestGate = new(MaxConcurrentRequests, MaxConcurrentRequests);

        Task<RadikoDateScheduleResult>[] tasks = Enumerable.Range(0, 7)
            .Select(index => LoadDateWithGateAsync(normalizedWeekStart.AddDays(index)))
            .ToArray();

        RadikoDateScheduleResult[] dateResults = await Task.WhenAll(tasks).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        List<ProgramInfo> programs = dateResults
            .SelectMany(result => result.Programs)
            .OrderBy(program => program.BroadcastDate)
            .ThenBy(program => program.StartMinutes)
            .ThenBy(program => program.StationId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<DateTime> failedDates = dateResults
            .Where(result => !result.Loaded)
            .Select(result => result.Date)
            .OrderBy(date => date)
            .ToList();

        int cacheCount = dateResults.Count(result => result.UsedCache);
        return new RadikoWeekScheduleResult(
            programs,
            dateResults.Length - failedDates.Count,
            cacheCount,
            failedDates);

        async Task<RadikoDateScheduleResult> LoadDateWithGateAsync(DateTime date)
        {
            await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await LoadDateAsync(
                        date,
                        stationIds,
                        forceRefresh,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                requestGate.Release();
            }
        }
    }

    private async Task<RadikoDateScheduleResult> LoadDateAsync(
        DateTime date,
        HashSet<string> stationIds,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        string cachePath = Path.Combine(_cacheDirectory, $"{date:yyyyMMdd}_{AreaId}.xml");
        byte[]? cachedBytes = await ReadCacheAsync(cachePath, cancellationToken).ConfigureAwait(false);

        if (!forceRefresh && cachedBytes is not null && IsCacheFresh(cachePath, date))
        {
            return await ParseDateAsync(
                    cachedBytes,
                    date,
                    stationIds,
                    usedCache: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            string url = $"https://radiko.jp/v3/program/date/{date:yyyyMMdd}/{AreaId}.xml";
            using HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));

            using HttpResponseMessage response = await HttpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            byte[] bytes = await response.Content
                .ReadAsByteArrayAsync(cancellationToken)
                .ConfigureAwait(false);
            await WriteCacheAsync(cachePath, bytes, cancellationToken).ConfigureAwait(false);

            return await ParseDateAsync(
                    bytes,
                    date,
                    stationIds,
                    usedCache: false,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            if (cachedBytes is not null)
            {
                return await ParseDateAsync(
                        cachedBytes,
                        date,
                        stationIds,
                        usedCache: true,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return new RadikoDateScheduleResult(date, [], false, false);
        }
    }

    private static Task<RadikoDateScheduleResult> ParseDateAsync(
        byte[] xmlBytes,
        DateTime broadcastDate,
        HashSet<string> stationIds,
        bool usedCache,
        CancellationToken cancellationToken) =>
        Task.Run(
            () => ParseDate(
                xmlBytes,
                broadcastDate,
                stationIds,
                usedCache,
                cancellationToken),
            cancellationToken);

    private static RadikoDateScheduleResult ParseDate(
        byte[] xmlBytes,
        DateTime broadcastDate,
        HashSet<string> stationIds,
        bool usedCache,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using MemoryStream stream = new(xmlBytes, writable: false);
            XDocument document = XDocument.Load(stream, LoadOptions.None);
            List<ProgramInfo> programs = [];

            IEnumerable<XElement> stationElements = document
                .Descendants()
                .Where(element => element.Name.LocalName.Equals("station", StringComparison.OrdinalIgnoreCase));

            foreach (XElement stationElement in stationElements)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string stationId = stationElement.Attribute("id")?.Value.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(stationId) || !stationIds.Contains(stationId))
                {
                    continue;
                }

                foreach (XElement programElement in stationElement
                             .Descendants()
                             .Where(element => element.Name.LocalName.Equals("prog", StringComparison.OrdinalIgnoreCase)))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ProgramInfo? program = ParseProgram(programElement, stationId, broadcastDate);
                    if (program is not null)
                    {
                        programs.Add(program);
                    }
                }
            }

            return new RadikoDateScheduleResult(broadcastDate, programs, true, usedCache);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new RadikoDateScheduleResult(broadcastDate, [], false, usedCache);
        }
    }

    private static ProgramInfo? ParseProgram(
        XElement programElement,
        string stationId,
        DateTime broadcastDate)
    {
        string fromText = programElement.Attribute("ft")?.Value.Trim() ?? string.Empty;
        string toText = programElement.Attribute("to")?.Value.Trim() ?? string.Empty;
        if (!TryParseRadikoDateTime(fromText, out DateTime start) ||
            !TryParseRadikoDateTime(toText, out DateTime end))
        {
            return null;
        }

        string title = ReadElement(programElement, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string performers = ReadElement(programElement, "pfm");
        string description = JoinDescription(
            ReadElement(programElement, "desc"),
            ReadElement(programElement, "info"));
        string programUrl = ReadElement(programElement, "url");
        string hashtag = ExtractHashtag(description);

        return new ProgramInfo
        {
            StationId = stationId,
            BroadcastDate = broadcastDate.Date,
            Day = broadcastDate.DayOfWeek,
            Start = FormatBroadcastTime(start, broadcastDate),
            End = FormatBroadcastTime(end, broadcastDate),
            Title = title,
            Performers = performers,
            Hashtag = hashtag,
            SearchKeyword = title,
            ProgramUrl = programUrl,
            RadikoUrl = $"https://radiko.jp/share/?sid={Uri.EscapeDataString(stationId)}&t={fromText}",
            Description = description
        };
    }

    private static string ReadElement(XElement parent, string localName)
    {
        XElement? element = parent.Elements()
            .FirstOrDefault(child => child.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));
        return CleanText(element?.Value ?? string.Empty);
    }

    private static string CleanText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string decoded = WebUtility.HtmlDecode(value);
        string withoutTags = HtmlTagRegex.Replace(decoded, " ");
        return WhiteSpaceRegex.Replace(withoutTags, " ").Trim();
    }

    private static string JoinDescription(string description, string information)
    {
        string combined = string.Join(
            " ",
            new[] { description, information }.Where(value => !string.IsNullOrWhiteSpace(value)));

        const int maxLength = 1200;
        return combined.Length <= maxLength
            ? combined
            : combined[..maxLength] + "…";
    }

    private static string ExtractHashtag(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        foreach (Match match in HashtagRegex.Matches(value))
        {
            string hashtag = match.Value;
            if (hashtag.Equals("#radiko", StringComparison.OrdinalIgnoreCase) ||
                hashtag.Equals("#ラジコ", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return hashtag;
        }

        return string.Empty;
    }

    private static bool TryParseRadikoDateTime(string value, out DateTime result) =>
        DateTime.TryParseExact(
            value,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out result);

    private static string FormatBroadcastTime(DateTime time, DateTime broadcastDate)
    {
        int dayOffset = (time.Date - broadcastDate.Date).Days;
        int hour = time.Hour + (Math.Max(0, dayOffset) * 24);
        return $"{hour:00}:{time.Minute:00}";
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClientHandler handler = new()
        {
            AutomaticDecompression = DecompressionMethods.GZip |
                                     DecompressionMethods.Deflate |
                                     DecompressionMethods.Brotli
        };

        HttpClient client = new(handler)
        {
            Timeout = TimeSpan.FromSeconds(25)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) RadioPulseViewer/1.0");
        return client;
    }

    private static bool IsCacheFresh(string path, DateTime date)
    {
        DateTime lastWrite = File.GetLastWriteTimeUtc(path);
        TimeSpan age = DateTime.UtcNow - lastWrite;
        TimeSpan lifetime = date.Date < DateTime.Today
            ? PastCacheLifetime
            : CurrentCacheLifetime;
        return age >= TimeSpan.Zero && age <= lifetime;
    }

    private static async Task<byte[]?> ReadCacheAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private static async Task WriteCacheAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        string temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(temporaryPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record RadikoDateScheduleResult(
        DateTime Date,
        List<ProgramInfo> Programs,
        bool Loaded,
        bool UsedCache);
}

public sealed record RadikoWeekScheduleResult(
    List<ProgramInfo> Programs,
    int LoadedDayCount,
    int CacheDayCount,
    List<DateTime> FailedDates);
