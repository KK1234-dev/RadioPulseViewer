using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using RadioPulseViewer.Models;

namespace RadioPulseViewer.Services;

public sealed class XPostCountService
{
    private const string PrimaryTokenEnvironmentVariable = "RADIOPULSE_X_BEARER_TOKEN";
    private const string FallbackTokenEnvironmentVariable = "X_BEARER_TOKEN";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MaximumRecentRange = TimeSpan.FromDays(7) - TimeSpan.FromMinutes(2);

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly Dictionary<string, CachedResult> _cache = new(StringComparer.Ordinal);

    public bool IsConfigured => !string.IsNullOrWhiteSpace(GetBearerToken());

    public string CredentialDescription =>
        $"環境変数 {PrimaryTokenEnvironmentVariable}（または {FallbackTokenEnvironmentVariable}）";

    public async Task<XPostCountResult> GetRecentCountsAsync(
        string query,
        TimeSpan requestedRange,
        CancellationToken cancellationToken)
    {
        string normalizedQuery = query.Trim();
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            throw new ArgumentException("検索語を入力してください。", nameof(query));
        }

        if (normalizedQuery.Length > 512)
        {
            throw new ArgumentException("検索語は512文字以内にしてください。", nameof(query));
        }

        string bearerToken = GetBearerToken() ??
            throw new InvalidOperationException(
                $"X APIのBearer Tokenが設定されていません。{CredentialDescription}を設定し、アプリを再起動してください。");

        TimeSpan range = NormalizeRange(requestedRange);
        string granularity = range <= TimeSpan.FromDays(1) ? "hour" : "day";
        string cacheKey = $"{normalizedQuery}\n{range.Ticks}\n{granularity}";

        if (_cache.TryGetValue(cacheKey, out CachedResult? cached) &&
            DateTimeOffset.UtcNow - cached.CachedAt < CacheDuration)
        {
            return cached.Result;
        }

        DateTimeOffset endTime = DateTimeOffset.UtcNow.AddSeconds(-30);
        DateTimeOffset startTime = endTime - range;

        string requestUrl = BuildRequestUrl(normalizedQuery, startTime, endTime, granularity);
        using HttpRequestMessage request = new(HttpMethod.Get, requestUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using HttpResponseMessage response = await HttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, responseBody, response.Headers);
        }

        XPostCountsApiResponse? apiResponse = JsonSerializer.Deserialize<XPostCountsApiResponse>(
            responseBody,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (apiResponse is null)
        {
            throw new InvalidOperationException("X APIの応答を解析できませんでした。");
        }

        if (apiResponse.Errors is { Count: > 0 })
        {
            string details = string.Join(
                " / ",
                apiResponse.Errors.Select(error => error.Detail ?? error.Title).Where(text => !string.IsNullOrWhiteSpace(text)));
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(details)
                    ? "X APIからエラーが返されました。"
                    : $"X APIからエラーが返されました: {details}");
        }

        List<XPostCountBucket> buckets = (apiResponse.Data ?? [])
            .OrderBy(item => item.Start)
            .Select(item => new XPostCountBucket
            {
                Start = item.Start,
                End = item.End,
                PostCount = item.PostCount
            })
            .ToList();

        long total = apiResponse.Meta?.TotalPostCount ?? buckets.Sum(item => item.PostCount);
        XPostCountResult result = new()
        {
            Query = normalizedQuery,
            EffectiveQuery = normalizedQuery,
            RetrievedAt = DateTimeOffset.Now,
            RangeStart = startTime,
            RangeEnd = endTime,
            Buckets = buckets,
            TotalPostCount = total
        };

        _cache[cacheKey] = new CachedResult(DateTimeOffset.UtcNow, result);
        return result;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new()
        {
            BaseAddress = new Uri("https://api.x.com/"),
            Timeout = TimeSpan.FromSeconds(25)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RadioPulseViewer", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string? GetBearerToken()
    {
        string? token = Environment.GetEnvironmentVariable(PrimaryTokenEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(token))
        {
            return token.Trim();
        }

        token = Environment.GetEnvironmentVariable(FallbackTokenEnvironmentVariable);
        return string.IsNullOrWhiteSpace(token) ? null : token.Trim();
    }

    private static TimeSpan NormalizeRange(TimeSpan requestedRange)
    {
        if (requestedRange <= TimeSpan.Zero)
        {
            return TimeSpan.FromHours(24);
        }

        return requestedRange > MaximumRecentRange ? MaximumRecentRange : requestedRange;
    }

    private static string BuildRequestUrl(
        string query,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        string granularity)
    {
        static string FormatUtc(DateTimeOffset value) =>
            value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        return "2/tweets/counts/recent" +
               $"?query={Uri.EscapeDataString(query)}" +
               $"&start_time={Uri.EscapeDataString(FormatUtc(startTime))}" +
               $"&end_time={Uri.EscapeDataString(FormatUtc(endTime))}" +
               $"&granularity={Uri.EscapeDataString(granularity)}";
    }

    private static Exception CreateApiException(
        HttpStatusCode statusCode,
        string responseBody,
        HttpResponseHeaders responseHeaders)
    {
        string? detail = TryReadErrorDetail(responseBody);
        string suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $"\n{detail}";

        return statusCode switch
        {
            HttpStatusCode.Unauthorized => new InvalidOperationException(
                "X APIの認証に失敗しました。Bearer Tokenを確認してください。" + suffix),
            HttpStatusCode.Forbidden => new InvalidOperationException(
                "X APIの利用権限がありません。Developer PortalのApp設定と利用プランを確認してください。" + suffix),
            HttpStatusCode.TooManyRequests => new InvalidOperationException(
                "X APIのレート制限に達しました。時間を置いて再実行してください。" +
                GetRateLimitResetText(responseHeaders) + suffix),
            _ => new HttpRequestException(
                $"X APIの呼び出しに失敗しました（HTTP {(int)statusCode} {statusCode}）。{suffix}",
                null,
                statusCode)
        };
    }

    private static string? TryReadErrorDetail(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return null;
        }

        try
        {
            XPostCountsApiResponse? parsed = JsonSerializer.Deserialize<XPostCountsApiResponse>(responseBody);
            string? detail = parsed?.Errors?
                .Select(error => error.Detail ?? error.Title)
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text));
            if (!string.IsNullOrWhiteSpace(detail))
            {
                return detail;
            }

            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (document.RootElement.TryGetProperty("detail", out JsonElement detailElement))
            {
                return detailElement.GetString();
            }

            if (document.RootElement.TryGetProperty("title", out JsonElement titleElement))
            {
                return titleElement.GetString();
            }
        }
        catch (JsonException)
        {
            // HTMLや未知の形式をログへ出さず、HTTPステータスだけを利用します。
        }

        return null;
    }

    private static string GetRateLimitResetText(HttpResponseHeaders responseHeaders)
    {
        if (!responseHeaders.TryGetValues("x-rate-limit-reset", out IEnumerable<string>? values) ||
            !long.TryParse(values.FirstOrDefault(), out long unixSeconds))
        {
            return string.Empty;
        }

        try
        {
            DateTimeOffset reset = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime();
            return $" 再試行目安: {reset:M/d H:mm}";
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    private sealed record CachedResult(DateTimeOffset CachedAt, XPostCountResult Result);
}
