using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using RadioPulseViewer.Models;
using RadioPulseViewer.Services;

namespace RadioPulseViewer;

public partial class MainWindow : Window
{
    private const string YahooRealtimeHomeUrl = "https://search.yahoo.co.jp/realtime";
    private const string TokyoRadikoUrl = "https://radiko.jp/index/JP13/";

    private readonly ProgramCatalogService _catalogService = new();
    private readonly RadikoScheduleService _radikoScheduleService = new();
    private readonly DispatcherTimer _searchDebounceTimer;

    private ProgramCatalog _catalog = new();
    private List<ProgramInfo> _fallbackPrograms = [];
    private Dictionary<string, StationInfo> _stationsById = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _weekStart = GetMonday(DateTime.Today);
    private ScheduledProgram? _selectedProgram;
    private CancellationTokenSource? _scheduleLoadCancellation;
    private CancellationTokenSource? _viewRefreshCancellation;
    private Task? _webViewInitializationTask;
    private bool _webViewReady;
    private bool _webViewEventsAttached;
    private bool _isScheduleLoading;
    private string? _pendingNavigationUrl;

    public MainWindow()
    {
        InitializeComponent();

        _searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(300)
        };
        _searchDebounceTimer.Tick += SearchDebounceTimer_Tick;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (!LoadCatalog())
        {
            return;
        }

        // まず同梱データを軽量表示し、その後に最新の週間番組表へ差し替えます。
        await RefreshWeekScheduleAsync();
        await LoadSelectedWeekAsync(forceRefresh: false);
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        _scheduleLoadCancellation?.Cancel();
        _viewRefreshCancellation?.Cancel();

        if (_webViewEventsAttached && ReactionWebView.CoreWebView2 is not null)
        {
            ReactionWebView.CoreWebView2.NewWindowRequested -= CoreWebView2_NewWindowRequested;
            ReactionWebView.NavigationCompleted -= ReactionWebView_NavigationCompleted;
        }

        ReactionWebView.Dispose();
    }

    private bool LoadCatalog()
    {
        try
        {
            _catalog = _catalogService.Load();
            _fallbackPrograms = _catalog.Programs.ToList();
            _stationsById = _catalog.Stations.ToDictionary(
                station => station.Id,
                station => station,
                StringComparer.OrdinalIgnoreCase);

            List<StationFilterItem> stationFilters =
            [
                new StationFilterItem { Id = string.Empty, DisplayName = "すべての放送局" }
            ];

            stationFilters.AddRange(_catalog.Stations
                .OrderBy(station => station.Name, StringComparer.CurrentCulture)
                .Select(station => new StationFilterItem
                {
                    Id = station.Id,
                    DisplayName = station.DisplayName
                }));

            StationFilterComboBox.ItemsSource = stationFilters;
            StationFilterComboBox.SelectedIndex = 0;

            CatalogNoticeTextBlock.Text =
                "東京エリア15局の番組表をradikoから読み込みます。";
            StatusTextBlock.Text = "同梱番組表を表示しています。";
            return true;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "番組データの読込みに失敗しました。";
            MessageBox.Show(
                this,
                ex.Message,
                "番組データ読込みエラー",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private async Task LoadSelectedWeekAsync(bool forceRefresh)
    {
        _scheduleLoadCancellation?.Cancel();
        CancellationTokenSource cancellation = new();
        _scheduleLoadCancellation = cancellation;
        SetScheduleLoading(isLoading: true);

        string range = $"{_weekStart:yyyy/M/d}～{_weekStart.AddDays(6):yyyy/M/d}";
        CatalogNoticeTextBlock.Text = $"radiko番組表を取得中: {range}";
        StatusTextBlock.Text = "週間番組表をバックグラウンドで取得しています...";

        try
        {
            RadikoWeekScheduleResult result = await _radikoScheduleService.LoadWeekAsync(
                _weekStart,
                _catalog.Stations,
                forceRefresh,
                cancellation.Token);

            cancellation.Token.ThrowIfCancellationRequested();

            if (result.Programs.Count == 0)
            {
                _catalog.Programs = _fallbackPrograms.ToList();
                CatalogNoticeTextBlock.Text =
                    $"radiko番組表を取得できませんでした: {range} / 初期データを表示";
                await RefreshWeekScheduleAsync();
                StatusTextBlock.Text =
                    "選択した週の番組表を取得できませんでした。radikoで公開されている期間を確認してください。";
                return;
            }

            _catalog.Programs = result.Programs;
            _selectedProgram = null;
            ClearSelectedProgram();
            await RefreshWeekScheduleAsync();

            string cacheText = result.CacheDayCount > 0
                ? $" / キャッシュ{result.CacheDayCount}日"
                : string.Empty;
            string missingText = result.FailedDates.Count > 0
                ? $" / 未取得: {string.Join(", ", result.FailedDates.Select(date => date.ToString("M/d")))}"
                : string.Empty;

            CatalogNoticeTextBlock.Text =
                $"radiko番組表: {range} / {result.LoadedDayCount}/7日 / {result.Programs.Count}番組{cacheText}{missingText}";
            StatusTextBlock.Text = result.FailedDates.Count == 0
                ? $"週間全番組を読み込みました: {result.Programs.Count}件"
                : $"取得可能な番組を読み込みました: {result.Programs.Count}件";
        }
        catch (OperationCanceledException)
        {
            // 週切替や終了で新しい処理へ移った場合は表示しません。
        }
        catch (Exception ex)
        {
            _catalog.Programs = _fallbackPrograms.ToList();
            await RefreshWeekScheduleAsync();
            CatalogNoticeTextBlock.Text =
                $"radiko番組表の取得に失敗: {range} / 初期データを表示";
            StatusTextBlock.Text = $"週間番組表の取得に失敗しました: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_scheduleLoadCancellation, cancellation))
            {
                _scheduleLoadCancellation = null;
                SetScheduleLoading(isLoading: false);
            }

            cancellation.Dispose();
        }
    }

    private void SetScheduleLoading(bool isLoading)
    {
        _isScheduleLoading = isLoading;
        PreviousWeekButton.IsEnabled = !isLoading;
        CurrentWeekButton.IsEnabled = !isLoading;
        NextWeekButton.IsEnabled = !isLoading;
        RefreshScheduleButton.IsEnabled = !isLoading;
        RefreshScheduleButton.Content = isLoading ? "取得中..." : "番組表更新";
    }

    private async Task<bool> EnsureWebViewReadyAsync()
    {
        if (_webViewReady)
        {
            return true;
        }

        Task initializationTask = _webViewInitializationTask ??= InitializeWebViewCoreAsync();
        await initializationTask;

        if (!_webViewReady && ReferenceEquals(_webViewInitializationTask, initializationTask))
        {
            // 初期化失敗後は、次回操作時に再試行できるようにします。
            _webViewInitializationTask = null;
        }

        return _webViewReady;
    }

    private async Task InitializeWebViewCoreAsync()
    {
        try
        {
            BrowserStatusBorder.Visibility = Visibility.Visible;
            BrowserStatusTextBlock.Text = "WebView2を初期化しています...";

            await ReactionWebView.EnsureCoreWebView2Async();
            _webViewReady = true;

            ReactionWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            ReactionWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;

            if (!_webViewEventsAttached)
            {
                ReactionWebView.CoreWebView2.NewWindowRequested += CoreWebView2_NewWindowRequested;
                ReactionWebView.NavigationCompleted += ReactionWebView_NavigationCompleted;
                _webViewEventsAttached = true;
            }

            BrowserStatusBorder.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _webViewReady = false;
            BrowserStatusBorder.Visibility = Visibility.Visible;
            BrowserStatusTextBlock.Text =
                "WebView2を起動できませんでした。Microsoft Edge WebView2 Runtimeを確認してください。\n\n" +
                ex.Message;
            StatusTextBlock.Text = "WebView2の初期化に失敗しました。";
        }
    }

    private void CoreWebView2_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        OpenExternalUrl(e.Uri);
    }

    private void ReactionWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            StatusTextBlock.Text = $"Webページを表示できませんでした: {e.WebErrorStatus}";
            return;
        }

        StatusTextBlock.Text = "Webページを表示しました。ページ内容の自動抽出は行いません。";
    }

    private async Task RefreshWeekScheduleAsync()
    {
        if (_catalog.Stations.Count == 0)
        {
            return;
        }

        _viewRefreshCancellation?.Cancel();
        CancellationTokenSource cancellation = new();
        _viewRefreshCancellation = cancellation;

        string selectedStationId =
            (StationFilterComboBox.SelectedItem as StationFilterItem)?.Id ?? string.Empty;
        string searchText = ProgramSearchTextBox.Text.Trim();
        DateTime weekStart = _weekStart;
        DateTime now = DateTime.Now;
        ProgramInfo[] programSnapshot = _catalog.Programs.ToArray();
        Dictionary<string, StationInfo> stationSnapshot = _stationsById;

        try
        {
            WeekScheduleBuildResult result = await Task.Run(
                () => BuildWeekSchedule(
                    programSnapshot,
                    stationSnapshot,
                    weekStart,
                    selectedStationId,
                    searchText,
                    now,
                    cancellation.Token),
                cancellation.Token);

            if (!ReferenceEquals(_viewRefreshCancellation, cancellation))
            {
                return;
            }

            WeekItemsControl.ItemsSource = result.Days;
            WeekRangeTextBlock.Text = $"{weekStart:yyyy/M/d} ～ {weekStart.AddDays(6):yyyy/M/d}";

            if (!_isScheduleLoading)
            {
                StatusTextBlock.Text = result.VisibleProgramCount == 0
                    ? "条件に一致する番組がありません。"
                    : $"番組 {result.VisibleProgramCount} 件を表示しています。";
            }
        }
        catch (OperationCanceledException)
        {
            // 新しい絞り込み処理へ切り替わった場合は表示しません。
        }
        finally
        {
            if (ReferenceEquals(_viewRefreshCancellation, cancellation))
            {
                _viewRefreshCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private static WeekScheduleBuildResult BuildWeekSchedule(
        IReadOnlyList<ProgramInfo> programs,
        IReadOnlyDictionary<string, StationInfo> stationsById,
        DateTime weekStart,
        string selectedStationId,
        string searchText,
        DateTime now,
        CancellationToken cancellationToken)
    {
        List<ScheduledProgram>[] programBuckets = Enumerable.Range(0, 7)
            .Select(_ => new List<ScheduledProgram>())
            .ToArray();

        foreach (ProgramInfo program in programs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!stationsById.TryGetValue(program.StationId, out StationInfo? station) || station is null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(selectedStationId) &&
                !program.StationId.Equals(selectedStationId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!MatchesSearch(program, searchText))
            {
                continue;
            }

            DateTime broadcastDate = program.BroadcastDate?.Date ??
                                     weekStart.AddDays(GetDayOffset(program.Day));
            int dayIndex = (broadcastDate - weekStart.Date).Days;
            if (dayIndex < 0 || dayIndex >= programBuckets.Length)
            {
                continue;
            }

            programBuckets[dayIndex].Add(CreateScheduledProgram(program, station, broadcastDate, now));
        }

        List<DaySchedule> days = new(7);
        int visibleProgramCount = 0;

        for (int index = 0; index < programBuckets.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<ScheduledProgram> dayPrograms = programBuckets[index];
            dayPrograms.Sort(static (left, right) =>
            {
                int timeComparison = left.Program.StartMinutes.CompareTo(right.Program.StartMinutes);
                return timeComparison != 0
                    ? timeComparison
                    : StringComparer.CurrentCulture.Compare(left.Station.ShortName, right.Station.ShortName);
            });

            DateTime date = weekStart.AddDays(index);
            visibleProgramCount += dayPrograms.Count;
            days.Add(new DaySchedule
            {
                Date = date,
                Header = GetJapaneseDayName(date.DayOfWeek),
                SubHeader = date.ToString("M/d"),
                Programs = dayPrograms
            });
        }

        return new WeekScheduleBuildResult(days, visibleProgramCount);
    }

    private static int GetDayOffset(DayOfWeek dayOfWeek) =>
        ((int)dayOfWeek + 6) % 7;

    private static ScheduledProgram CreateScheduledProgram(
        ProgramInfo program,
        StationInfo station,
        DateTime date,
        DateTime now)
    {
        DateTime start = date.Date.AddMinutes(program.StartMinutes);
        DateTime end = date.Date.AddMinutes(program.EndMinutes);
        if (end <= start)
        {
            end = end.AddDays(1);
        }

        return new ScheduledProgram
        {
            Program = program,
            Station = station,
            BroadcastDate = date,
            TimeText = $"{program.Start}～{program.End}",
            IsOnAir = now >= start && now < end
        };
    }

    private static bool MatchesSearch(ProgramInfo program, string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return program.Title.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
               program.Performers.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
               program.Hashtag.Contains(searchText, StringComparison.CurrentCultureIgnoreCase) ||
               program.SearchKeyword.Contains(searchText, StringComparison.CurrentCultureIgnoreCase);
    }

    private async Task SelectProgramAsync(ScheduledProgram scheduledProgram)
    {
        _selectedProgram = scheduledProgram;
        ProgramInfo program = scheduledProgram.Program;
        StationInfo station = scheduledProgram.Station;

        SelectedProgramTitleTextBlock.Text = program.Title;
        SelectedProgramMetaTextBlock.Text =
            $"{station.Name} / {scheduledProgram.BroadcastDate:M/d(ddd)} {program.Start}～{program.End}" +
            (string.IsNullOrWhiteSpace(program.Performers) ? string.Empty : $"\n出演: {program.Performers}") +
            (string.IsNullOrWhiteSpace(program.Hashtag) ? string.Empty : $"\n検索タグ: {program.Hashtag}");
        SelectedProgramDescriptionTextBlock.Text = string.IsNullOrWhiteSpace(program.Description)
            ? "番組を選択すると、番組名または登録済みタグでYahoo!リアルタイム検索を表示します。"
            : program.Description;

        ReactionKeywordTextBox.Text = program.EffectiveSearchKeyword;
        await ShowReactionAsync(program.EffectiveSearchKeyword);
    }

    private void ClearSelectedProgram()
    {
        SelectedProgramTitleTextBlock.Text = "番組を選択してください";
        SelectedProgramMetaTextBlock.Text = string.Empty;
        SelectedProgramDescriptionTextBlock.Text =
            "週間番組表から番組を選択すると、Yahoo!リアルタイム検索を表示します。";
        ReactionKeywordTextBox.Text = string.Empty;
    }

    private async Task ShowReactionAsync(string keyword)
    {
        string normalizedKeyword = keyword.Trim();
        if (string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            MessageBox.Show(this, "検索語を入力してください。", "検索語", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        string url = "https://search.yahoo.co.jp/realtime/search?p=" +
                     Uri.EscapeDataString(normalizedKeyword) +
                     "&ei=UTF-8";
        StatusTextBlock.Text = $"Yahoo!リアルタイム検索を表示しています: {normalizedKeyword}";
        await NavigateInWebViewAsync(url);
    }

    private async Task NavigateInWebViewAsync(string url)
    {
        if (!TryValidateWebUrl(url, out Uri? uri))
        {
            MessageBox.Show(this, "URLが正しくありません。", "URLエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _pendingNavigationUrl = uri.AbsoluteUri;
        if (!await EnsureWebViewReadyAsync())
        {
            return;
        }

        string targetUrl = _pendingNavigationUrl ?? uri.AbsoluteUri;
        _pendingNavigationUrl = null;
        ReactionWebView.Source = new Uri(targetUrl, UriKind.Absolute);
    }

    private static bool TryValidateWebUrl(string url, [NotNullWhen(true)] out Uri? uri)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            return true;
        }

        uri = null;
        return false;
    }

    private StationInfo? GetCurrentStation()
    {
        if (_selectedProgram is not null)
        {
            return _selectedProgram.Station;
        }

        string stationId = (StationFilterComboBox.SelectedItem as StationFilterItem)?.Id ?? string.Empty;
        return _stationsById.GetValueOrDefault(stationId);
    }

    private async void ProgramButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: ScheduledProgram scheduledProgram })
        {
            await SelectProgramAsync(scheduledProgram);
        }
    }

    private async void StationFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _selectedProgram = null;
        ClearSelectedProgram();
        await RefreshWeekScheduleAsync();
    }

    private void ProgramSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private async void SearchDebounceTimer_Tick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        await RefreshWeekScheduleAsync();
    }

    private async void PreviousWeekButton_Click(object sender, RoutedEventArgs e)
    {
        _weekStart = _weekStart.AddDays(-7);
        await LoadSelectedWeekAsync(forceRefresh: false);
    }

    private async void CurrentWeekButton_Click(object sender, RoutedEventArgs e)
    {
        _weekStart = GetMonday(DateTime.Today);
        await LoadSelectedWeekAsync(forceRefresh: false);
    }

    private async void NextWeekButton_Click(object sender, RoutedEventArgs e)
    {
        _weekStart = _weekStart.AddDays(7);
        await LoadSelectedWeekAsync(forceRefresh: false);
    }

    private async void RefreshScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        await LoadSelectedWeekAsync(forceRefresh: true);
    }

    private async void ShowReactionButton_Click(object sender, RoutedEventArgs e)
    {
        await ShowReactionAsync(ReactionKeywordTextBox.Text);
    }

    private void OpenRadikoButton_Click(object sender, RoutedEventArgs e)
    {
        StationInfo? station = GetCurrentStation();
        string url = _selectedProgram?.Program.RadikoUrl ?? string.Empty;
        if (string.IsNullOrWhiteSpace(url))
        {
            url = station?.RadikoUrl ?? TokyoRadikoUrl;
        }

        OpenExternalUrl(url);
    }

    private void OpenProgramSiteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProgram is null || string.IsNullOrWhiteSpace(_selectedProgram.Program.ProgramUrl))
        {
            MessageBox.Show(this, "この番組の公式サイトURLは登録されていません。", "番組公式サイト", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenExternalUrl(_selectedProgram.Program.ProgramUrl);
    }

    private async void OpenStationScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenStationScheduleAsync(GetCurrentStation());
    }

    private async void OpenSelectedStationScheduleButton_Click(object sender, RoutedEventArgs e)
    {
        await OpenStationScheduleAsync(GetCurrentStation());
    }

    private async Task OpenStationScheduleAsync(StationInfo? station)
    {
        string? url = station?.OfficialScheduleUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            url = station?.RadikoUrl;
        }

        await NavigateInWebViewAsync(string.IsNullOrWhiteSpace(url) ? TokyoRadikoUrl : url);
    }

    private void BrowserBackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_webViewReady && ReactionWebView.CanGoBack)
        {
            ReactionWebView.GoBack();
        }
    }

    private void BrowserForwardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_webViewReady && ReactionWebView.CanGoForward)
        {
            ReactionWebView.GoForward();
        }
    }

    private void BrowserRefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_webViewReady)
        {
            ReactionWebView.CoreWebView2.Reload();
        }
    }

    private void OpenExternalBrowserButton_Click(object sender, RoutedEventArgs e)
    {
        string? url = ReactionWebView.Source?.AbsoluteUri ?? _pendingNavigationUrl;
        OpenExternalUrl(string.IsNullOrWhiteSpace(url) ? YahooRealtimeHomeUrl : url);
    }

    private void OpenExternalUrl(string url)
    {
        if (!TryValidateWebUrl(url, out Uri? uri))
        {
            MessageBox.Show(this, "URLが正しくありません。", "URLエラー", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ブラウザ起動エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static DateTime GetMonday(DateTime date)
    {
        int offset = ((int)date.DayOfWeek + 6) % 7;
        return date.Date.AddDays(-offset);
    }

    private static string GetJapaneseDayName(DayOfWeek dayOfWeek) => dayOfWeek switch
    {
        DayOfWeek.Monday => "月曜日",
        DayOfWeek.Tuesday => "火曜日",
        DayOfWeek.Wednesday => "水曜日",
        DayOfWeek.Thursday => "木曜日",
        DayOfWeek.Friday => "金曜日",
        DayOfWeek.Saturday => "土曜日",
        DayOfWeek.Sunday => "日曜日",
        _ => string.Empty
    };

    private sealed record WeekScheduleBuildResult(
        List<DaySchedule> Days,
        int VisibleProgramCount);
}
