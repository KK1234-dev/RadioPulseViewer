using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using RadioPulseViewer.Models;
using RadioPulseViewer.Services;

namespace RadioPulseViewer;

public partial class PublicGraphRecordWindow : Window
{
    private const string SourceName = "Yahoo!リアルタイム検索（画面確認）";
    private readonly PublicGraphRecordService _recordService = new();
    private readonly string _initialQuery;

    public PublicGraphRecordWindow(string initialQuery)
    {
        InitializeComponent();
        _initialQuery = initialQuery.Trim();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        QueryTextBox.Text = _initialQuery;
        HistoryPathTextBlock.Text = $"CSV: {_recordService.CsvPath}";
        StatusTextBlock.Text =
            "参照ページのグラフにマウスポインタを合わせ、表示された件数を入力してください。ページ内容は自動取得しません。";
    }

    private void OpenSourceButton_Click(object sender, RoutedEventArgs e)
    {
        string query = QueryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show(
                this,
                "検索語を入力してください。",
                "検索語",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        string url = "https://search.yahoo.co.jp/realtime/search?p=" +
                     Uri.EscapeDataString(query) +
                     "&ei=UTF-8";

        try
        {
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
            StatusTextBlock.Text = "参照ページを既定ブラウザーで開きました。";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "参照ページを開けませんでした",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PublicGraphRecord record = BuildRecord();
            ShowPreview(record);
            StatusTextBlock.Text = "入力内容をプレビューしました。まだCSVには保存していません。";
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            StatusTextBlock.Text = ex.Message;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            PublicGraphRecord record = BuildRecord();
            ShowPreview(record);
            await _recordService.AppendAsync(record, CancellationToken.None);
            HistoryPathTextBlock.Text = $"CSV: {_recordService.CsvPath}";
            StatusTextBlock.Text =
                $"{record.RangeLabel}の記録をCSVへ保存しました。自動抽出ではなく、画面で確認して入力した値です。";
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            StatusTextBlock.Text = ex.Message;
        }
    }

    private PublicGraphRecord BuildRecord()
    {
        string query = QueryTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new InvalidOperationException("検索語を入力してください。");
        }

        if (query.Length > 512)
        {
            throw new InvalidOperationException("検索語は512文字以内にしてください。");
        }

        IReadOnlyList<PublicGraphPoint> points = _recordService.ParsePoints(PointInputTextBox.Text);
        long? enteredTotal = _recordService.ParseOptionalTotal(TotalCountTextBox.Text);
        if (points.Count == 0 && enteredTotal is null)
        {
            throw new InvalidOperationException("時系列データまたは合計件数を入力してください。");
        }

        string note = NoteTextBox.Text.Trim();
        if (note.Length > 500)
        {
            throw new InvalidOperationException("メモは500文字以内にしてください。");
        }

        long total = enteredTotal ?? points.Sum(point => point.PostCount);
        return new PublicGraphRecord
        {
            Source = SourceName,
            Query = query,
            RangeLabel = GetSelectedRangeLabel(),
            RecordedAt = DateTimeOffset.Now,
            TotalPostCount = total,
            Points = points,
            Note = note
        };
    }

    private string GetSelectedRangeLabel() =>
        (RangeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "未指定";

    private void ShowPreview(PublicGraphRecord record)
    {
        IReadOnlyList<PreviewPoint> previewPoints = BuildPreviewPoints(record);
        PreviewItemsControl.ItemsSource = previewPoints;
        SummaryTextBlock.Text =
            $"{record.RangeLabel} / 合計 {record.TotalPostCount:N0}件 / {record.Points.Count}点";
    }

    private static IReadOnlyList<PreviewPoint> BuildPreviewPoints(PublicGraphRecord record)
    {
        IReadOnlyList<PublicGraphPoint> sourcePoints = record.Points.Count == 0
            ?
            [
                new PublicGraphPoint
                {
                    Order = 1,
                    Label = "合計",
                    PostCount = record.TotalPostCount
                }
            ]
            : record.Points;

        long maximum = Math.Max(1, sourcePoints.Max(point => point.PostCount));
        return sourcePoints.Select(point =>
        {
            double width = point.PostCount == 0
                ? 0
                : Math.Max(2, Math.Round(460d * point.PostCount / maximum, 1));

            return new PreviewPoint
            {
                Label = point.Label,
                CountText = $"{point.PostCount:N0}件",
                BarWidth = width,
                ToolTip = $"{point.Label}: {point.PostCount:N0}件"
            };
        }).ToList();
    }

    private void OpenHistoryFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _recordService.EnsureDirectoryExists();
            Process.Start(new ProcessStartInfo(_recordService.DirectoryPath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "保存先を開けませんでした",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class PreviewPoint
    {
        public required string Label { get; init; }

        public required string CountText { get; init; }

        public double BarWidth { get; init; }

        public required string ToolTip { get; init; }
    }
}
