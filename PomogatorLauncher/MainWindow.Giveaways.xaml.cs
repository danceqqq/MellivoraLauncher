using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using PomogatorLauncher.Giveaway;
using System.Diagnostics;

namespace PomogatorLauncher;

public partial class MainWindow
{
    private bool _giveawaysTgStatWebViewInitDone;

    private readonly ObservableCollection<GiveawayStripItemVm> _giveawayStripItems = new();
    private readonly ObservableCollection<GiveawayPostTileVm> _giveawayPostTiles = new();
    private CancellationTokenSource? _giveawayScanCts;

    private void OpenGiveawaysPanel()
    {
        GiveawaysOverlay.Visibility = Visibility.Visible;
        GiveawaysStripItems.ItemsSource = _giveawayStripItems;
        GiveawaysPostTiles.ItemsSource = _giveawayPostTiles;
        _giveawayScanCts?.Cancel();
        _giveawayScanCts = new CancellationTokenSource();
        var token = _giveawayScanCts.Token;
        GiveawaysStatusText.Text = "Сканирую tgstat.ru (и при пустом ответе — запасной t.me/s/…), ~30 дней…";
        _ = RunGiveawayScanAsync(token);
    }

    private async Task RunGiveawayScanAsync(CancellationToken ct)
    {
        var progress = new Progress<string>(s =>
        {
            if (!string.IsNullOrEmpty(s))
                GiveawaysStatusText.Text = s;
        });

        try
        {
            var summary = await GiveawayTelegramScanService
                .ScanIntoAsync(_giveawayStripItems, _giveawayPostTiles, progress, ct)
                .ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            if (summary.StatusOverrideMessage != null)
            {
                GiveawaysStatusText.Text = summary.StatusOverrideMessage;
                return;
            }

            GiveawaysStatusText.Text = summary.PostTileCount == 0
                ? "За месяц постов с «розыгрыш» не найдено. Лента показывает отслеживаемых блогеров."
                : $"Найдено записей: {summary.PostTileCount}.";
        }
        catch (OperationCanceledException)
        {
            /* закрыли панель или новый запуск */
        }
        catch (Exception ex)
        {
            GiveawaysStatusText.Text = "Ошибка: " + ex.Message;
        }
    }

    private void GiveawaysHomeButton_Click(object sender, RoutedEventArgs e)
    {
        _giveawayScanCts?.Cancel();
        _giveawayScanCts = null;
        GiveawaysTgStatAuthOverlay.Visibility = Visibility.Collapsed;
        GiveawaysOverlay.Visibility = Visibility.Collapsed;
    }

    private async void GiveawaysTgStatDebug_Click(object sender, RoutedEventArgs e)
    {
        GiveawaysTgStatAuthOverlay.Visibility = Visibility.Visible;
        try
        {
            await InitGiveawaysTgStatWebViewAsync().ConfigureAwait(true);
        }
        catch
        {
            /* WebView2 не поднялся */
        }
    }

    private void GiveawaysTgStatAuthClose_Click(object sender, RoutedEventArgs e)
    {
        GiveawaysTgStatAuthOverlay.Visibility = Visibility.Collapsed;
    }

    private async void GiveawaysTgStatAuthApplyCookies_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var core = GiveawaysTgStatWebView.CoreWebView2;
            if (core == null)
            {
                MessageBox.Show(this, "Подождите загрузки страницы в окне ниже.", "TGStat", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var cm = core.CookieManager;
            var raw = new List<CoreWebView2Cookie>();
            foreach (var uri in new[] { "https://tgstat.ru/", "https://www.tgstat.ru/" })
            {
                var batch = await cm.GetCookiesAsync(uri).ConfigureAwait(true);
                foreach (var c in batch)
                    raw.Add(c);
            }

            var deduped = raw
                .GroupBy(c => (c.Name, Domain: c.Domain ?? "", Path: c.Path ?? "/"))
                .Select(g => g.Last())
                .ToList();

            TgStatSessionHttp.ReplaceCookiesFromWebView2(deduped);

            MessageBox.Show(
                this,
                $"В HttpClient для tgstat перенесено куков: {TgStatSessionHttp.LastImportedCookieCount}. " +
                "Закройте панель «Дебаг» и при необходимости снова откройте «Список розыгрышей», чтобы пересканировать.",
                "TGStat",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            GiveawaysTgStatAuthOverlay.Visibility = Visibility.Collapsed;
            _giveawayScanCts?.Cancel();
            _giveawayScanCts = new CancellationTokenSource();
            var token = _giveawayScanCts.Token;
            GiveawaysStatusText.Text = "Повторное сканирование с учётом куков tgstat…";
            _ = RunGiveawayScanAsync(token);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не удалось прочитать куки: " + ex.Message, "TGStat", MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async Task InitGiveawaysTgStatWebViewAsync()
    {
        if (_giveawaysTgStatWebViewInitDone) return;

        var userData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PomogatorLauncher",
            "WebView2TgStat");
        Directory.CreateDirectory(userData);
        var env = await CoreWebView2Environment.CreateAsync(null, userData).ConfigureAwait(true);
        await GiveawaysTgStatWebView.EnsureCoreWebView2Async(env).ConfigureAwait(true);
        _giveawaysTgStatWebViewInitDone = true;
        GiveawaysTgStatWebView.Source = new Uri("https://tgstat.ru/");
    }

    private void GiveawayOpenPost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var url = btn.Tag as string;
        if (string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            /* ignore */
        }
    }
}
