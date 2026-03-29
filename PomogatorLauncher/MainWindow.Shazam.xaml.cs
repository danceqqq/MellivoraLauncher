using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PomogatorLauncher;

public partial class MainWindow
{
    private readonly ObservableCollection<ShazamTrackViewModel> _shazamHistoryItems = new();
    private readonly ObservableCollection<ShazamTrackViewModel> _shazamMarqueeItems = new();
    private Storyboard? _shazamMarqueeStoryboard;
    private DispatcherTimer? _shazamMarqueeDebounce;
    private CancellationTokenSource? _shazamUserStopCts;
    private bool _shazamRecordingActive;

    private void InitShazamMarquee()
    {
        ShazamHistoryMarquee.ItemsSource = _shazamMarqueeItems;
        _shazamHistoryItems.CollectionChanged += (_, _) => ScheduleShazamMarqueeRebuild();
    }

    private void ScheduleShazamMarqueeRebuild()
    {
        if (_shazamMarqueeDebounce == null)
        {
            _shazamMarqueeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            _shazamMarqueeDebounce.Tick += OnShazamMarqueeDebounceTick;
        }

        _shazamMarqueeDebounce.Stop();
        _shazamMarqueeDebounce.Start();
    }

    private void OnShazamMarqueeDebounceTick(object? sender, EventArgs e)
    {
        _shazamMarqueeDebounce?.Stop();
        RebuildShazamMarquee();
    }

    private void RebuildShazamMarquee()
    {
        _shazamMarqueeStoryboard?.Stop();
        ShazamMarqueeTranslate.X = 0;
        _shazamMarqueeItems.Clear();
        var n = _shazamHistoryItems.Count;
        if (n == 0)
            return;
        foreach (var x in _shazamHistoryItems)
            _shazamMarqueeItems.Add(x);
        foreach (var x in _shazamHistoryItems)
            _shazamMarqueeItems.Add(x);

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, RestartShazamMarqueeAnimation);
    }

    private void RestartShazamMarqueeAnimation()
    {
        _shazamMarqueeStoryboard?.Stop();
        ShazamMarqueeTranslate.X = 0;
        var n = _shazamHistoryItems.Count;
        if (n == 0 || !IsLoaded)
            return;

        const double stepPx = 268d + 16d;
        var dist = n * stepPx;
        var seconds = Math.Clamp(n * 7.5, 42, 95);
        var anim = new DoubleAnimation(0, -dist, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever };
        _shazamMarqueeStoryboard = new Storyboard();
        Storyboard.SetTarget(anim, ShazamMarqueeTranslate);
        Storyboard.SetTargetProperty(anim, new PropertyPath(TranslateTransform.XProperty));
        _shazamMarqueeStoryboard.Children.Add(anim);
        _shazamMarqueeStoryboard.Begin();
    }

    private void ShazamOnWindowClosed()
    {
        try
        {
            _shazamUserStopCts?.Cancel();
        }
        catch
        {
            /* ignore */
        }

        _shazamMarqueeStoryboard?.Stop();
        StopShazamPulseSafely();
    }

    private void OpenShazamPanel()
    {
        ShazamOverlay.Visibility = Visibility.Visible;
        ShazamHistoryStore.LoadInto(_shazamHistoryItems);
        ShazamResultPanel.Visibility = Visibility.Collapsed;
        ShazamHeroCover.Source = null;
        ShazamStatusText.Text =
            "Нажмите кнопку — начнётся непрерывное прослушивание звука с ПК; ShazamIO ищет трек каждые несколько секунд. Нажмите снова, чтобы остановить.";
        StopShazamPulseSafely();
    }

    private void ShazamHomeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _shazamUserStopCts?.Cancel();
        }
        catch
        {
            /* ignore */
        }

        ShazamOverlay.Visibility = Visibility.Collapsed;
        StopShazamPulseSafely();
        _shazamRecordingActive = false;
    }

    private void ShazamRecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_shazamRecordingActive)
        {
            _shazamUserStopCts?.Cancel();
            return;
        }

        _shazamRecordingActive = true;
        ShazamStatusText.Text = "Слушаю эфир и ищу трек (ShazamIO)… Повторное нажатие — стоп.";
        StartShazamPulse();
        _ = RunShazamRealtimeListenAsync();
    }

    private void StartShazamPulse()
    {
        try
        {
            if (ShazamPulseHost.FindResource("ShazamPulseStoryboard") is Storyboard sb)
                sb.Begin(ShazamPulseHost, true);
        }
        catch
        {
            /* ignore */
        }
    }

    private void StopShazamPulseSafely()
    {
        try
        {
            if (ShazamPulseHost.FindResource("ShazamPulseStoryboard") is Storyboard sb)
                sb.Stop(ShazamPulseHost);
        }
        catch
        {
            /* ignore */
        }
    }

    private static bool IsFatalShazamIoError(string? error)
    {
        if (string.IsNullOrEmpty(error))
            return false;
        return error.Contains("Не найден Python", StringComparison.Ordinal)
               || error.Contains("recognize_stdin", StringComparison.Ordinal)
               || error.Contains("Не найден Assets", StringComparison.Ordinal)
               || error.Contains("pip install shazamio", StringComparison.OrdinalIgnoreCase)
               || error.Contains("Установите: pip install shazamio", StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunShazamRealtimeListenAsync()
    {
        CancellationTokenSource? session = null;
        var foundMatch = false;
        var showIdleMessageInFinally = true;
        try
        {
            using var maxCts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            session = new CancellationTokenSource();
            _shazamUserStopCts = session;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(session.Token, maxCts.Token);

            using var ring = new LoopbackRingBuffer(14);
            try
            {
                ring.Start();
            }
            catch (Exception ex)
            {
                showIdleMessageInFinally = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    ShazamStatusText.Text = "Захват звука с ПК: " + ex.Message;
                    StopShazamPulseSafely();
                    _shazamRecordingActive = false;
                });
                return;
            }

            await Dispatcher.InvokeAsync(() =>
                ShazamStatusText.Text =
                    "Слушаю в реальном времени… ShazamIO опрашивает эфир. Повторное нажатие — стоп.");

            try
            {
                await Task.Delay(2500, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            while (!linked.IsCancellationRequested)
            {
                // Не читать IsLoaded / элементы окна здесь: после ConfigureAwait(false) мы не на UI-потоке.
                byte[] wav;
                try
                {
                    wav = ring.GetLastSecondsAsWav(10);
                }
                catch (Exception ex)
                {
                    showIdleMessageInFinally = false;
                    await Dispatcher.InvokeAsync(() =>
                        ShazamStatusText.Text = "Подготовка снимка аудио: " + ex.Message);
                    break;
                }

                if (wav.Length < 2000)
                {
                    try
                    {
                        await Task.Delay(600, linked.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                await Dispatcher.InvokeAsync(() => ShazamStatusText.Text = "Ищу через ShazamIO…");

                SongRecognitionResult result;
                try
                {
                    result = await ShazamIoRecognitionService.RecognizeWavBytesAsync(wav, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (result.IsSuccess)
                {
                    foundMatch = true;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        ShazamResultPanel.Visibility = Visibility.Visible;
                        ShazamHeroTitle.Text = result.Title;
                        ShazamHeroArtist.Text = result.Artist;
                        SetShazamHeroCoverAsync(result.CoverUrl);
                        ShazamStatusText.Text = "Найдено. Нажмите кнопку, чтобы искать снова.";
                        var vm = new ShazamTrackViewModel(result.Title, result.Artist, result.CoverUrl, DateTime.UtcNow);
                        ShazamHistoryStore.PrependAndSave(_shazamHistoryItems, vm);
                    });
                    break;
                }

                if (IsFatalShazamIoError(result.Error))
                {
                    showIdleMessageInFinally = false;
                    await Dispatcher.InvokeAsync(() => ShazamStatusText.Text = result.Error ?? "Ошибка ShazamIO.");
                    break;
                }

                if (string.Equals(result.Error, ShazamIoRecognitionService.NoMatchMarker, StringComparison.Ordinal))
                {
                    await Dispatcher.InvokeAsync(() =>
                        ShazamStatusText.Text = "Пока не распознано — продолжаю слушать…");
                }
                else
                {
                    await Dispatcher.InvokeAsync(() =>
                        ShazamStatusText.Text = (result.Error ?? "Ошибка") + " — продолжаю слушать…");
                }

                try
                {
                    await Task.Delay(3200, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            session?.Dispose();
            if (ReferenceEquals(_shazamUserStopCts, session))
                _shazamUserStopCts = null;

            await Dispatcher.InvokeAsync(() =>
            {
                StopShazamPulseSafely();
                _shazamRecordingActive = false;
                if (showIdleMessageInFinally && !foundMatch && ShazamResultPanel.Visibility != Visibility.Visible)
                    ShazamStatusText.Text = "Остановлено. Нажмите кнопку для нового поиска.";
            });
        }
    }

    private async void SetShazamHeroCoverAsync(string? url)
    {
        ShazamHeroCover.Source = null;
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "PomogatorLauncher/1.0");
            var bytes = await http.GetByteArrayAsync(uri).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.StreamSource = new MemoryStream(bytes);
                    img.DecodePixelWidth = 256;
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.EndInit();
                    img.Freeze();
                    ShazamHeroCover.Source = img;
                }
                catch
                {
                    /* ignore */
                }
            });
        }
        catch
        {
            /* ignore */
        }
    }
}
