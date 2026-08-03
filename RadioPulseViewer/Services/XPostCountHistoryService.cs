using System.Collections.Concurrent;
using System.IO;
using System.Text;
using RadioPulseViewer.Models;

namespace RadioPulseViewer.Services;

public sealed class XPostCountHistoryService
{
    private static readonly ConcurrentDictionary<string, byte> SavedSnapshots = new(StringComparer.Ordinal);
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public XPostCountHistoryService()
    {
        DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadioPulseViewer",
            "XPostCounts");
        CsvPath = Path.Combine(DirectoryPath, "x-post-counts.csv");
    }

    public string DirectoryPath { get; }

    public string CsvPath { get; }

    public async Task<bool> AppendAsync(XPostCountResult result, CancellationToken cancellationToken)
    {
        string snapshotKey = $"{result.RetrievedAt:O}\n{result.Query}\n{result.RangeStart:O}\n{result.RangeEnd:O}";
        if (!SavedSnapshots.TryAdd(snapshotKey, 0))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(DirectoryPath);
            bool needsHeader = !File.Exists(CsvPath) || new FileInfo(CsvPath).Length == 0;

            await using FileStream stream = new(
                CsvPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            await using StreamWriter writer = new(stream, Utf8WithBom);

            if (needsHeader)
            {
                await writer.WriteLineAsync(
                    "retrieved_at,query,effective_query,range_start,range_end,bucket_start,bucket_end,post_count");
            }

            foreach (XPostCountBucket bucket in result.Buckets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string line = string.Join(
                    ',',
                    Escape(result.RetrievedAt.ToString("O")),
                    Escape(result.Query),
                    Escape(result.EffectiveQuery),
                    Escape(result.RangeStart.ToUniversalTime().ToString("O")),
                    Escape(result.RangeEnd.ToUniversalTime().ToString("O")),
                    Escape(bucket.Start.ToUniversalTime().ToString("O")),
                    Escape(bucket.End.ToUniversalTime().ToString("O")),
                    bucket.PostCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
                await writer.WriteLineAsync(line);
            }

            await writer.FlushAsync(cancellationToken);
            return true;
        }
        catch
        {
            SavedSnapshots.TryRemove(snapshotKey, out _);
            throw;
        }
    }

    public void EnsureDirectoryExists() => Directory.CreateDirectory(DirectoryPath);

    private static string Escape(string value)
    {
        if (value.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
