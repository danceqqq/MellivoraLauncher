using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using PomogatorLauncher.Giveaway;

namespace PomogatorLauncher;

public partial class MainWindow
{
    private bool _tgBypassSettingsSuppress;

    private void SyncTgBypassSettingsUi()
    {
        _tgBypassSettingsSuppress = true;
        try
        {
            var mode = AppSettingsStore.LoadTelegramBypassMode();
            var tag = mode switch
            {
                TelegramBypassMode.TgWsProxy => "tgWs",
                TelegramBypassMode.ZapretTelegram => "zapret",
                _ => "none"
            };
            foreach (var o in TelegramBypassModeCombo.Items)
            {
                if (o is ComboBoxItem it && string.Equals(it.Tag as string, tag, StringComparison.Ordinal))
                {
                    TelegramBypassModeCombo.SelectedItem = it;
                    break;
                }
            }
        }
        finally
        {
            _tgBypassSettingsSuppress = false;
        }
    }

    private void ApplyTgBypassFromSavedSettings()
    {
        ApplyTelegramBypassMode(AppSettingsStore.LoadTelegramBypassMode(), quiet: true);
    }

    /// <param name="quiet">Без окон при автозапуске (нет exe / bat).</param>
    private void ApplyTelegramBypassMode(TelegramBypassMode mode, bool quiet)
    {
        TgWsProxyHost.StopIfStartedByUs();
        ZapretTelegramHost.StopIfStartedByUs();

        switch (mode)
        {
            case TelegramBypassMode.None:
                TelegramPublicChannelParser.SetUseSocks5Proxy(false);
                return;

            case TelegramBypassMode.TgWsProxy:
                TelegramPublicChannelParser.SetUseSocks5Proxy(true);
                if (TgWsProxyHost.TryStart(out var tgErr))
                    return;
                if (!quiet)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = TgWsProxyHost.ReleasesUrl,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        /* ignore */
                    }

                    MessageBox.Show(
                        this,
                        tgErr + Environment.NewLine + Environment.NewLine +
                        "Открыта страница релизов TG WS Proxy. Положите TgWsProxy_windows.exe в mellivoravpn\\tg_ws_proxy рядом с PomogatorLauncher.exe (папка копируется из проекта при сборке) и выберите режим снова.",
                        "Обход блокировки Телеграм",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;

            case TelegramBypassMode.ZapretTelegram:
                TelegramPublicChannelParser.SetUseSocks5Proxy(false);
                if (ZapretTelegramHost.TryStartGeneralBat(out var zErr))
                    return;
                if (!quiet)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = ZapretTelegramHost.RepoUrl,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        /* ignore */
                    }

                    MessageBox.Show(
                        this,
                        zErr + Environment.NewLine + Environment.NewLine +
                        "Открыт репозиторий zapret-telegram. Распакуйте в mellivoravpn\\zapret_telegram (нужны general.bat и папка bin). Часто требуется запуск от имени администратора.",
                        "Zapret Telegram",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
        }
    }

    private void TelegramBypassModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_tgBypassSettingsSuppress) return;
        if (TelegramBypassModeCombo.SelectedItem is not ComboBoxItem it || it.Tag is not string tag)
            return;

        var mode = tag switch
        {
            "tgWs" => TelegramBypassMode.TgWsProxy,
            "zapret" => TelegramBypassMode.ZapretTelegram,
            _ => TelegramBypassMode.None
        };

        AppSettingsStore.SaveTelegramBypassMode(mode);
        ApplyTelegramBypassMode(mode, quiet: false);
    }

    private void TgBypassDocLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
        }
        catch
        {
            /* ignore */
        }
    }

    private void RefreshZapretServiceButtonUi()
    {
        var installed = ZapretServiceManager.IsZapretServiceInstalled();
        ZapretServiceButtonText.Text = installed ? "Удалить сервис" : "Установка сервиса";
        ZapretServiceButtonIcon.Source = new Uri(
            installed
                ? "pack://application:,,,/svg/delete.svg"
                : "pack://application:,,,/svg/install.svg");
        ZapretServiceActionButton.ToolTip = installed
            ? "Остановить и удалить службу zapret и связанные драйверы (как в service.bat → Remove Services)."
            : "Установить службу zapret с профилем general (ALT).bat — UAC, права администратора.";
    }

    private async void ZapretServiceActionButton_Click(object sender, RoutedEventArgs e)
    {
        var root = ZapretTelegramHost.FindZapretRoot();
        if (string.IsNullOrEmpty(root))
        {
            MessageBox.Show(
                this,
                "Не найдена папка mellivoravpn\\zapret_telegram с general.bat и bin.",
                "Zapret",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var installed = ZapretServiceManager.IsZapretServiceInstalled();
        ZapretServiceActionButton.IsEnabled = false;
        try
        {
            string? err = null;
            var ok = await Task.Run(() =>
                installed
                    ? ZapretServiceManager.TryRemoveZapretService(out err)
                    : ZapretServiceManager.TryInstallZapretService(root, out err)).ConfigureAwait(true);

            RefreshZapretServiceButtonUi();

            if (!ok)
            {
                MessageBox.Show(
                    this,
                    string.IsNullOrEmpty(err) ? "Операция не выполнена." : err,
                    installed ? "Удаление сервиса" : "Установка сервиса",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            ZapretServiceActionButton.IsEnabled = true;
        }
    }
}
