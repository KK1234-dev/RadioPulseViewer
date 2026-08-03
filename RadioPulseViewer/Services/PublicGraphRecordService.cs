using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using RadioPulseViewer.Models;

namespace RadioPulseViewer.Services;

public sealed class PublicGraphRecordService
{
    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly SemaphoreSlim FileLock = new(1, 1);
    private static readonly Regex DelimitedLine = new(
        @"^(?<label>.+?)\s*[,、\t]\s*(?<count>[0-9][0-9,]*)\s*(?:件)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceLine = new(
        @"^(?<label>.+)\s+(?<count>[0-9][0-9,]*)\s*(?:件)?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public PublicGraphRecordService()
    {
        DirectoryPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadioPulseViewer",
            "PublicGraphRecords");
        CsvPath = Path.Combine(DirectoryPath, "public-graph-records.csv");
    }

    public string DirectoryPath { get; }

    public string CsvPath { get; }

    public IReadOnlyList<PublicGraphPoint> ParsePoints(string input)
    {
        string[] lines = input.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        if (lines.Length > 500)
        {
            throw new FormatException("入力できるデータ点は500件までです。");
        }

        List<PublicGraphPoint> points = [];
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            Match match = DelimitedLine.Match(line);
            if (!match.Success)
            {
                match = WhitespaceLine.Match(line);
            }

            if (!match.Success)
            {
                throw new FormatException(
                    $"{index + 1}行目を読み取れません。『表示ラベル, 件数』の形式で入力してください。");
            }

            string label = match.Groups["label"].Value.Trim();
            string countText = match.Groups["count"].Value.Replace(",", string.Empty, StringComparison.Ordinal);
            if (string.IsNullOrWhiteSpace(label) ||
                !long.TryParse(countText, NumberStyles.None, CultureInfo.InvariantCulture, out long count) ||
                count < 0)
            {
                throw new FormatException($"{index + 1}行目の表示ラベルまたは件数が正しくありません。");
            }

            points.Add(new PublicGraphPoint
            {
                Order = points.Count + 1,
                Label = label,
                PostCount = count
            });
        }

        return points;
    }

    public long? ParseOptionalTotal(string input)
    {
        string normalized = input.Trim()
            .Replace(",", string.Empty, StringComparison.Ordinal)
            .Replace("件", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (!long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out long total) || total < 0)
        {
            throw new FormatException("合計件数は0以上の整数で入力してください。");
        }

        return total;
    }

    public async Task AppendAsync(PublicGraphRecord record, CancellationToken cancellationToken)
    {
        await FileLock.WaitAsync(cancellationToken);
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
                    "recorded_at,source,query,range,total_post_count,point_order,point_label,post_count,note");
            }

            if (record.Points.Count == 0)
            {
                await writer.WriteLineAsync(BuildCsvLine(record, null));
            }
            else
            {
                foreach (PublicGraphPoint point in record.Points)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(BuildCsvLine(record, point));
                }
            }

            await writer.FlushAsync(cancellationToken);
        }
        finally
        {
            FileLock.Release();
        }
    }

    public void EnsureDirectoryExists() => Directory.CreateDirectory(DirectoryPath);

    private static string BuildCsvLine(PublicGraphRecord record, PublicGraphPoint? point) =>
        string.Join(
            ',',
            Escape(record.RecordedAt.ToString("O", CultureInfo.InvariantCulture)),
            Escape(record.Source),
            Escape(record.Query),
            Escape(record.RangeLabel),
            record.TotalPostCount.ToString(CultureInfo.InvariantCulture),
            point?.Order.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(point?.Label ?? string.Empty),
            point?.PostCount.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Escape(record.Note));

    private static string Escape(string value)
    {
        string safe = value;
        if (!string.IsNullOrEmpty(safe) && "=+-@\t\r".IndexOf(safe[0]) >= 0)
        {
            safe = "'" + safe;
        }

        if (safe.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return safe;
        }

        return $"\"{safe.Replace("\"", "\"\"")}\"";
    }
}
