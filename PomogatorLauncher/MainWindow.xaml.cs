using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace PomogatorLauncher;

public partial class MainWindow : Window
{
    private LinearGradientBrush? _animatedBgBrush;
    private EventHandler? _renderHandler;
    private Storyboard? _statusPingStoryboard;
    private DispatcherTimer? _monitorTimer;
    private CancellationTokenSource? _extractDebounceCts;
    private bool _monitorWebViewInitDone;
    private bool _vehiclesWebViewInitDone;
    private bool _clothesWebViewInitDone;

    public MainWindow()
    {
        InitializeComponent();
        TrySetWindowIcon();
        InitShazamMarquee();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _animatedBgBrush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.RelativeToBoundingBox
        };
        _animatedBgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1d, 0x1d, 0x1d), 0));
        _animatedBgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xe0, 0x01, 0x5b), 0.32));
        _animatedBgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xf7, 0xf7, 0xf7), 0.5));
        _animatedBgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0xe0, 0x01, 0x5b), 0.68));
        _animatedBgBrush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1d, 0x1d, 0x1d), 1));
        AnimatedBackground.Background = _animatedBgBrush;

        _renderHandler = (_, _) => UpdateAnimatedBackground();
        CompositionTarget.Rendering += _renderHandler;

        _statusPingStoryboard = TryFindResource("StatusPingStoryboard") as Storyboard;

        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _monitorTimer.Tick += (_, _) =>
        {
            RefreshCommunityCounters();
            _ = RefreshMonitoringAsync();
        };
        _monitorTimer.Start();

        RefreshCommunityCounters();
        _ = TryHttpMonitoringFallbackAsync();
        _ = InitMonitorWebViewAsync();
        ApplyTgBypassFromSavedSettings();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _monitorTimer?.Stop();
        _monitorTimer = null;

        _extractDebounceCts?.Cancel();
        _extractDebounceCts = null;

        if (MonitorWebView.CoreWebView2 != null)
        {
            MonitorWebView.CoreWebView2.NavigationCompleted -= OnMonitorNavigationCompleted;
            MonitorWebView.CoreWebView2.DOMContentLoaded -= OnMonitorDomContentLoaded;
        }

        if (VehiclesWebView.CoreWebView2 != null)
            VehiclesWebView.CoreWebView2.NavigationCompleted -= OnVehiclesNavigationCompleted;

        if (ClothesWebView.CoreWebView2 != null)
            ClothesWebView.CoreWebView2.NavigationCompleted -= OnClothesNavigationCompleted;

        if (_renderHandler != null)
            CompositionTarget.Rendering -= _renderHandler;
        _renderHandler = null;
        _animatedBgBrush = null;

        if (_statusPingStoryboard != null)
            _statusPingStoryboard.Stop(this);
        _statusPingStoryboard = null;

        UnregisterGlobalHotkey();

        TgWsProxyHost.StopIfStartedByUs();
        ZapretTelegramHost.StopIfStartedByUs();
        MellivoraWarpHost.StopIfStartedByUs();

        ShazamOnWindowClosed();
    }

    private async Task InitMonitorWebViewAsync()
    {
        if (_monitorWebViewInitDone) return;
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PomogatorLauncher", "WebView2");
            Directory.CreateDirectory(userData);
            var env = await CoreWebView2Environment.CreateAsync(null, userData).ConfigureAwait(false);

            await MonitorWebView.EnsureCoreWebView2Async(env).ConfigureAwait(false);

            var core = MonitorWebView.CoreWebView2;
            core.NavigationCompleted += OnMonitorNavigationCompleted;
            core.DOMContentLoaded += OnMonitorDomContentLoaded;

            _monitorWebViewInitDone = true;
            MonitorWebView.Source = new Uri(FletcherMonitoring.MonitoringUrl);
        }
        catch
        {
            await Dispatcher.InvokeAsync(() =>
            {
                PlayerCountText.Text = "—";
                SetServerStatusVisual(false);
            });
            await TryHttpMonitoringFallbackAsync();
        }
    }

    private void RefreshCommunityCounters()
    {
        var dir = AppContext.BaseDirectory;
        DiscordCountText.Text = CounterJson.ReadDisplay(Path.Combine(dir, "counter", "discord.json"));
        FamilyCountText.Text = CounterJson.ReadDisplay(Path.Combine(dir, "counter", "family.json"));
    }

    private async Task RefreshMonitoringAsync()
    {
        if (MonitorWebView.CoreWebView2 != null)
        {
            try
            {
                MonitorWebView.Reload();
            }
            catch
            {
                await TryHttpMonitoringFallbackAsync();
            }

            return;
        }

        await InitMonitorWebViewAsync();
        if (MonitorWebView.CoreWebView2 == null)
            await TryHttpMonitoringFallbackAsync();
    }

    private void OnMonitorNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (MonitorWebView.CoreWebView2 == null) return;
        _ = ScheduleExtractMonitoringAsync();
    }

    private void OnMonitorDomContentLoaded(object? sender, CoreWebView2DOMContentLoadedEventArgs e)
    {
        if (MonitorWebView.CoreWebView2 == null) return;
        _ = ScheduleExtractMonitoringAsync();
    }

    private async Task ScheduleExtractMonitoringAsync()
    {
        if (MonitorWebView.CoreWebView2 == null) return;

        _extractDebounceCts?.Cancel();
        _extractDebounceCts = new CancellationTokenSource();
        var token = _extractDebounceCts.Token;

        int[] delaysMs = { 1500, 4000, 8000, 14000, 22000 };

        try
        {
            foreach (var ms in delaysMs)
            {
                await Task.Delay(ms, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;

                string? html = null;
                try
                {
                    var raw = await MonitorWebView.CoreWebView2.ExecuteScriptAsync(
                            "JSON.stringify(document.documentElement.innerHTML)")
                        .ConfigureAwait(false);
                    html = DecodeWebView2StringResult(raw);
                }
                catch
                {
                    continue;
                }

                var applied = await Dispatcher.InvokeAsync(() => TryApplyMonitoringFromFullHtml(html));
                if (applied) return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (PlayerCountText.Text == "…")
                {
                    PlayerCountText.Text = "—";
                    SetServerStatusVisual(false);
                }
            });
        }
        catch (OperationCanceledException)
        {
            /* новая навигация */
        }
    }

    /// <summary>Результат ExecuteScriptAsync — JSON-строка (в т.ч. с экранированием).</summary>
    private static string? DecodeWebView2StringResult(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Trim();
        if (raw == "null" || raw == "undefined") return null;

        try
        {
            return JsonSerializer.Deserialize<string>(raw);
        }
        catch
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                return doc.RootElement.GetString();
            }
            catch
            {
                if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
                    return JsonSerializer.Deserialize<string>(raw);
                return raw;
            }
        }
    }

    /// <returns>true если получилось разобрать Detroit (тот же парсер, что и для HTTP).</returns>
    private bool TryApplyMonitoringFromFullHtml(string? html)
    {
        try
        {
            if (string.IsNullOrEmpty(html)) return false;
            if (html.Length < 4000 && html.IndexOf("Detroit", StringComparison.Ordinal) < 0) return false;
            if (FletcherMonitoring.IsJavaScriptAntiBotShell(html)
                || FletcherMonitoring.IsLikelyChallengeOrBlockPage(html))
                return false;

            var players = FletcherMonitoring.TryParsePlayerCount(html);
            if (players == null) return false;

            PlayerCountText.Text = players;
            SetServerStatusVisual(FletcherMonitoring.ResolveServerOnline(html));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task TryHttpMonitoringFallbackAsync()
    {
        string? html = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            html = await FletcherMonitoring.Http.GetStringAsync(FletcherMonitoring.MonitoringUrl, cts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            html = null;
        }

        await Dispatcher.InvokeAsync(() => ApplyMonitoringFromHttp(html));
    }

    private void ApplyMonitoringFromHttp(string? html)
    {
        if (string.IsNullOrEmpty(html)
            || FletcherMonitoring.IsJavaScriptAntiBotShell(html)
            || FletcherMonitoring.IsLikelyChallengeOrBlockPage(html))
        {
            PlayerCountText.Text = "—";
            SetServerStatusVisual(false);
            return;
        }

        var players = FletcherMonitoring.TryParsePlayerCount(html);
        PlayerCountText.Text = players ?? "—";
        var online = FletcherMonitoring.ResolveServerOnline(html);
        SetServerStatusVisual(online);
    }

    private void SetServerStatusVisual(bool online)
    {
        if (_statusPingStoryboard != null)
            _statusPingStoryboard.Stop(this);
        StatusPingScale.ScaleX = StatusPingScale.ScaleY = 1;
        StatusPingEllipse.Opacity = 0;

        if (online)
        {
            StatusCoreEllipse.Fill = new SolidColorBrush(Color.FromRgb(0x22, 0xc5, 0x5e));
            StatusPingEllipse.Fill = new SolidColorBrush(Color.FromArgb(0x66, 0x34, 0xd3, 0x99));
            _statusPingStoryboard?.Begin(this);
        }
        else
        {
            StatusCoreEllipse.Fill = new SolidColorBrush(Color.FromRgb(0xef, 0x44, 0x44));
        }
    }

    private void UpdateAnimatedBackground()
    {
        if (_animatedBgBrush == null) return;

        var t = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        var angle = t * 0.32;
        var cos = Math.Cos(angle);
        var sin = Math.Sin(angle);
        var wobble = 0.07 * Math.Sin(t * 1.65);

        _animatedBgBrush.StartPoint = new Point(0.5 + 0.48 * cos, 0.5 + 0.48 * sin);
        _animatedBgBrush.EndPoint = new Point(0.5 - 0.48 * cos, 0.5 - 0.48 * sin);

        _animatedBgBrush.GradientStops[1].Offset = Math.Clamp(0.26 + wobble, 0.12, 0.44);
        _animatedBgBrush.GradientStops[2].Offset = Math.Clamp(0.5 + 0.04 * Math.Sin(t * 2.1), 0.42, 0.58);
        _animatedBgBrush.GradientStops[3].Offset = Math.Clamp(0.74 - wobble, 0.56, 0.88);
    }

    private void TrySetWindowIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
        if (!File.Exists(path)) return;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.EndInit();
        bmp.Freeze();
        Icon = bmp;
    }

    private void SectionTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var title = btn.Tag as string ?? "Раздел";
        if (title == "Библиотека авто")
        {
            _ = ShowVehiclesLibraryAsync();
            return;
        }

        if (title == "Библиотека одежды")
        {
            _ = ShowClothesLibraryAsync();
            return;
        }

        if (title == "Поиск Музыки Shazam")
        {
            OpenShazamPanel();
            return;
        }

        if (title == "Настройки приложения")
        {
            OpenSettingsPanel();
            return;
        }

        if (title == "Mellivora VPN")
        {
            OpenMellivoraVpnPanel();
            return;
        }

        if (title == "Список розыгрышей")
        {
            OpenGiveawaysPanel();
            return;
        }

        MessageBox.Show($"«{title}» скоро откроется здесь.", "Помогатор", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void VehiclesHomeButton_Click(object sender, RoutedEventArgs e)
    {
        VehiclesOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task ShowVehiclesLibraryAsync()
    {
        VehiclesOverlay.Visibility = Visibility.Visible;
        try
        {
            await InitVehiclesWebViewAsync().ConfigureAwait(true);
        }
        catch
        {
            // окно уже на экране; при ошибке WebView останется пустым
        }
    }

    private async Task InitVehiclesWebViewAsync()
    {
        if (_vehiclesWebViewInitDone) return;

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PomogatorLauncher",
            "WebView2Vehicles");
        Directory.CreateDirectory(folder);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: folder).ConfigureAwait(true);
        await VehiclesWebView.EnsureCoreWebView2Async(env).ConfigureAwait(true);

        var core = VehiclesWebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(MajesticWikiVehiclesChrome.HideHeaderOnCreateScript)
            .ConfigureAwait(true);

        core.NavigationCompleted += OnVehiclesNavigationCompleted;
        _vehiclesWebViewInitDone = true;
        VehiclesWebView.Source = new Uri(MajesticWikiVehiclesChrome.VehiclesUrl);
    }

    private async void OnVehiclesNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        try
        {
            await VehiclesWebView.ExecuteScriptAsync(MajesticWikiVehiclesChrome.HideHeaderKillOnlyScript)
                .ConfigureAwait(true);
        }
        catch
        {
            // ignore
        }
    }

    private void ClothesHomeButton_Click(object sender, RoutedEventArgs e)
    {
        ClothesOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task ShowClothesLibraryAsync()
    {
        ClothesOverlay.Visibility = Visibility.Visible;
        try
        {
            await InitClothesWebViewAsync().ConfigureAwait(true);
        }
        catch
        {
            /* WebView может остаться пустым */
        }
    }

    private async Task InitClothesWebViewAsync()
    {
        if (_clothesWebViewInitDone) return;

        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PomogatorLauncher",
            "WebView2Clothes");
        Directory.CreateDirectory(folder);

        var env = await CoreWebView2Environment.CreateAsync(userDataFolder: folder).ConfigureAwait(true);
        await ClothesWebView.EnsureCoreWebView2Async(env).ConfigureAwait(true);

        var core = ClothesWebView.CoreWebView2;
        core.Settings.IsStatusBarEnabled = false;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(MajesticWikiVehiclesChrome.HideHeaderOnCreateScript)
            .ConfigureAwait(true);

        core.NavigationCompleted += OnClothesNavigationCompleted;
        _clothesWebViewInitDone = true;
        ClothesWebView.Source = new Uri(MajesticWikiVehiclesChrome.ClothesMaleUrl);
    }

    private async void OnClothesNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess) return;
        try
        {
            await ClothesWebView.ExecuteScriptAsync(MajesticWikiVehiclesChrome.HideHeaderKillOnlyScript)
                .ConfigureAwait(true);
        }
        catch
        {
            /* ignore */
        }
    }
}
