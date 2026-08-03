using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using RadioPulseViewer.Models;
using RadioPulseViewer.Services;

namespace RadioPulseViewer;

public partial class XPostCountWindow : Window
{
    private readonly XPostCountService _postCountService = new();
    private readonly XPostCountHistoryService _historyService = new();
    private readonly string _initialQuery;
    private CancellationTokenSource? _requestCancellation;

    public XPostCountWindow(string initialQuery)
    {
        InitializeComponent();
        _initialQuery = initialQuery.Trim();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        QueryTextBox.Text = _initialQuery;
        HistoryPathTextBlock.Text = $"CSV: {_historyService.CsvPath}";

        StatusTextBlock.Text = _postCountService.IsConfigured
            ? "X APIの設定を確認しました。検索語と期間を指定して取得してください。"
            : $"X APIのBearer Tokenが未設定です。{_postCountService.CredentialDescription}を設定し、アプリを再起動してください。";
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = null;
    }

    private async void FetchButton_Click(object sender, RoutedEventArgs e)
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

        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        CancellationTokenSource cancellation = new();
        _requestCancellation = cancellation;

        FetchButton.IsEnabled = false;
        FetchButton.Content = "取得中...";
        StatusTextBlock.Text = "公式X APIから投稿数だけを取得しています...";

        try
        {
            TimeSpan range = GetSelectedRange();
            XPostCountResult result = await _postCountService.GetRecentCountsAsync(
                query,
                range,
                cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();

            TotalCountTextBlock.Text = result.TotalPostCount.ToString("N0");
            CountItemsControl.ItemsSource = BuildChartPoints(result.Buckets);

            DateTimeOffset localStart = result.RangeStart.ToLocalTime();
            DateTimeOffset localEnd = result.RangeEnd.ToLocalTime();
            SummaryTextBlock.Text =
                $"検索: {result.EffectiveQuery}\n" +
                $"対象: {localStart:M/d H:mm}～{localEnd:M/d H:mm}\n" +
                $"取得: {result.RetrievedAt:M/d H:mm:ss}";

            bool saved = await _historyService.AppendAsync(result, cancellation.Token);
            StatusTextBlock.Text = saved
                ? $"{result.Buckets.Count}区間の投稿数を取得し、CSVへ保存しました。Yahoo! JAPANのグラフ値ではなく、公式X APIの集計値です。"
                : $"{result.Buckets.Count}区間の投稿数を表示しました。同じ取得結果はCSVへ重複保存していません。";
            HistoryPathTextBlock.Text = $"CSV: {_historyService.CsvPath}";
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "投稿数の取得を中止しました。";
        }
        catch (Exception ex)
        {
            TotalCountTextBlock.Text = "-";
            CountItemsControl.ItemsSource = null;
            StatusTextBlock.Text = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_requestCancellation, cancellation))
            {
                _requestCancellation = null;
                FetchButton.IsEnabled = true;
                FetchButton.Content = "投稿数を取得";
            }

            cancellation.Dispose();
        }
    }

    private TimeSpan GetSelectedRange()
    {
        string? tag = (RangeComboBox.SelectedItem as ComboBoxItem)?.Tag as string;
        return tag switch
        {
            "6h" => TimeSpan.FromHours(6),
            "7d" => TimeSpan.FromDays(7),
            _ => TimeSpan.FromHours(24)
        };
    }

    private static IReadOnlyList<ChartPoint> BuildChartPoints(IReadOnlyList<XPostCountBucket> buckets)
    {
        if (buckets.Count == 0)
        {
            return [];
        }

        long maximum = Math.Max(1, buckets.Max(bucket => bucket.PostCount));
        return buckets.Select(bucket =>
        {
            DateTimeOffset localStart = bucket.Start.ToLocalTime();
            DateTimeOffset localEnd = bucket.End.ToLocalTime();
            bool dayBucket = bucket.End - bucket.Start >= TimeSpan.FromHours(20);
            double width = bucket.PostCount == 0
                ? 0
                : Math.Max(2, Math.Round(420d * bucket.PostCount / maximum, 1));

            return new ChartPoint
            {
                Label = dayBucket ? localStart.ToString("M/d") : localStart.ToString("M/d H:mm"),
                CountText = $"{bucket.PostCount:N0}件",
                BarWidth = width,
                ToolTip = $"{localStart:M/d H:mm}～{localEnd:M/d H:mm}: {bucket.PostCount:N0}件"
            };
        }).ToList();
    }

    private void OpenHistoryFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _historyService.EnsureDirectoryExists();
            Process.Start(new ProcessStartInfo(_historyService.DirectoryPath)
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

    private sealed class ChartPoint
    {
        public required string Label { get; init; }

        public required string CountText { get; init; }

        public double BarWidth { get; init; }

        public required string ToolTip { get; init; }
    }
}
