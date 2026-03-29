using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SharpVectors.Converters;

namespace PomogatorLauncher;

public partial class MainWindow
{
    private static readonly Uri MellivoraWarpCheckboxOff =
        new("pack://application:,,,/svg/checkboxoff.svg", UriKind.Absolute);
    private static readonly Uri MellivoraWarpCheckboxOn =
        new("pack://application:,,,/svg/checkboxon.svg", UriKind.Absolute);

    private string? _selectedMellivoraWarpPath;

    private void RefreshMellivoraWarpCfgUi()
    {
        MellivoraWarpCfgRowsPanel.Children.Clear();
        _selectedMellivoraWarpPath = null;

        var files = MellivoraWarpHost.EnumerateConfigFiles().ToList();
        if (files.Count == 0)
        {
            MellivoraWarpCfgRowsPanel.Children.Add(new TextBlock
            {
                Text = "В папке mellivoravpn\\warpcfg нет файлов .yaml / .yml / .conf.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            });
            SyncMellivoraWarpStatusText();
            UpdateMellivoraAmneziaLaunchButtonLabel();
            return;
        }

        var savedName = AppSettingsStore.LoadSelectedWarpCfgFileName();
        string? pick = null;
        if (!string.IsNullOrEmpty(savedName))
            pick = files.FirstOrDefault(f =>
                string.Equals(Path.GetFileName(f), savedName, StringComparison.OrdinalIgnoreCase));
        pick ??= files[0];
        _selectedMellivoraWarpPath = pick;

        foreach (var path in files)
            MellivoraWarpCfgRowsPanel.Children.Add(CreateMellivoraWarpCfgRow(path));

        UpdateMellivoraWarpCheckboxSources();
        SyncMellivoraWarpStatusText();
        UpdateMellivoraAmneziaLaunchButtonLabel();
    }

    private void UpdateMellivoraAmneziaLaunchButtonLabel()
    {
        var exe = AmneziaVpnLocator.FindExecutable();
        MellivoraAmneziaLaunchButtonText.Text = exe != null
            ? "Открыть Amnezia VPN"
            : "Скачать Amnezia VPN (клиент не найден)";
    }

    private UIElement CreateMellivoraWarpCfgRow(string fullPath)
    {
        var svg = new SvgViewbox
        {
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
            Source = MellivoraWarpCheckboxOff
        };
        var label = new TextBlock
        {
            Text = Path.GetFileName(fullPath),
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13,
            Foreground = new SolidColorBrush(Colors.WhiteSmoke),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(svg);
        sp.Children.Add(label);

        var btn = new Button
        {
            Content = sp,
            Tag = fullPath,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 8, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Cursor = Cursors.Hand,
            FocusVisualStyle = null
        };
        btn.Click += MellivoraWarpCfgRowButton_Click;
        return btn;
    }

    private void MellivoraWarpCfgRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button b || b.Tag is not string path)
            return;
        _selectedMellivoraWarpPath = path;
        AppSettingsStore.SaveSelectedWarpCfgFileName(Path.GetFileName(path));
        UpdateMellivoraWarpCheckboxSources();
    }

    private void UpdateMellivoraWarpCheckboxSources()
    {
        foreach (var child in MellivoraWarpCfgRowsPanel.Children)
        {
            if (child is not Button btn || btn.Tag is not string path)
                continue;
            if (btn.Content is not StackPanel sp || sp.Children.Count < 1 ||
                sp.Children[0] is not SvgViewbox svg)
                continue;
            var on = string.Equals(path, _selectedMellivoraWarpPath, StringComparison.OrdinalIgnoreCase);
            svg.Source = on ? MellivoraWarpCheckboxOn : MellivoraWarpCheckboxOff;
        }
    }

    private void SyncMellivoraWarpStatusText()
    {
        if (MellivoraWarpHost.IsOurInstanceRunning())
        {
            MellivoraWarpStatusText.Text =
                "mihomo запущен: mixed 127.0.0.1:20808 · SOCKS 127.0.0.1:20809 · API 127.0.0.1:20909";
            return;
        }

        var dir = MellivoraWarpHost.AppliedWorkDirectory;
        var warpcfg = MellivoraWarpHost.WarpcfgDirectory;
        var hasExe = MellivoraWarpHost.FindMihomoExecutable() != null;
        var parts = new List<string>();
        if (!hasExe)
            parts.Add("Mihomo не найден — «Скачать / переустановить» или положите mihomo*.exe в warpcfg.");
        if (Directory.Exists(dir))
            parts.Add("Копии конфигов: " + dir);
        parts.Add("warpcfg: " + warpcfg);
        MellivoraWarpStatusText.Text = string.Join(" ", parts);
    }

    /// <summary>
    /// Если mihomo уже есть (в т.ч. mihomo-windows-amd64-v3.exe в warpcfg) — не качаем; иначе — bootstrap в mihomo.exe.</summary>
    private async Task<bool> EnsureMihomoForYamlAsync(CancellationToken ct)
    {
        if (MellivoraWarpHost.FindMihomoExecutable() != null)
            return true;

        var warpcfg = MellivoraWarpHost.WarpcfgDirectory;
        var progress = new Progress<string>(s =>
        {
            try
            {
                MellivoraWarpStatusText.Text = s;
            }
            catch
            {
                /* ignore */
            }
        });

        var (ok, err) = await MihomoBootstrapper.EnsureReadyAsync(warpcfg, false, progress, ct)
            .ConfigureAwait(true);
        if (ok)
            return MellivoraWarpHost.FindMihomoExecutable() != null;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = MellivoraWarpHost.MihomoReleasesUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            /* ignore */
        }

        MessageBox.Show(this,
            err + Environment.NewLine + Environment.NewLine +
            "Открыта страница релиза Mihomo. Проверьте сеть или скачайте архив вручную.",
            "Дополнительные способы",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    private async void MellivoraWarpApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_selectedMellivoraWarpPath) || !File.Exists(_selectedMellivoraWarpPath))
        {
            MessageBox.Show(this,
                "Выберите файл в списке выше.",
                "Дополнительные способы",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        MellivoraWarpApplyButton.IsEnabled = false;
        try
        {
            var ext = Path.GetExtension(_selectedMellivoraWarpPath).ToLowerInvariant();
            if (ext is ".yaml" or ".yml")
            {
                if (!await EnsureMihomoForYamlAsync(CancellationToken.None).ConfigureAwait(true))
                    return;

                MellivoraWarpHost.WriteRuntimeYamlFromSource(
                    _selectedMellivoraWarpPath,
                    MellivoraWarpHost.RuntimeConfigPath);
                if (!MellivoraWarpHost.TryStartMihomo(MellivoraWarpHost.RuntimeConfigPath, out var err))
                {
                    MessageBox.Show(this,
                        err ?? "Не удалось запустить mihomo.",
                        "Дополнительные способы",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(this,
                        "Запущен mihomo с runtime.yaml. Прокси: 127.0.0.1:20808 (mixed) или SOCKS 20809.",
                        "Дополнительные способы",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else if (ext == ".conf")
            {
                MellivoraWarpHost.StopIfStartedByUs();
                MellivoraWarpHost.CopyConfToApplied(_selectedMellivoraWarpPath, "active.conf");
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "\"" + MellivoraWarpHost.AppliedWorkDirectory + "\"",
                        UseShellExecute = true
                    });
                }
                catch
                {
                    /* ignore */
                }

                var am = AmneziaVpnLocator.FindExecutable();
                if (am != null)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = am,
                            UseShellExecute = true
                        });
                    }
                    catch
                    {
                        /* ignore */
                    }
                }

                MessageBox.Show(this,
                    "Скопировано в active.conf. Папка профиля открыта в проводнике." +
                    (am != null
                        ? " Запущен Amnezia VPN — импортируйте профиль (AmneziaWG)."
                        : " Установите Amnezia VPN и импортируйте файл.") +
                    Environment.NewLine +
                    "Обычный WireGuard этот .conf не примет.",
                    "Дополнительные способы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    "Неподдерживаемое расширение: " + ext,
                    "Дополнительные способы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Дополнительные способы", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            MellivoraWarpApplyButton.IsEnabled = true;
            SyncMellivoraWarpStatusText();
        }
    }

    private void MellivoraWarpStopButton_Click(object sender, RoutedEventArgs e)
    {
        MellivoraWarpHost.StopIfStartedByUs();
        SyncMellivoraWarpStatusText();
    }

    private async void MellivoraAmneziaDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        MellivoraAmneziaDownloadButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(s => MellivoraWarpStatusText.Text = s);
            var (ok, err) = await AmneziaClientBootstrapper.DownloadLatestWindowsInstallerAndRunAsync(
                    progress,
                    CancellationToken.None)
                .ConfigureAwait(true);
            if (ok)
            {
                MessageBox.Show(this,
                    "Установщик скачан во временную папку и запущен. Следуйте шагам мастера Amnezia VPN.",
                    "Дополнительные способы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    err ?? "Ошибка.",
                    "Дополнительные способы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = AmneziaVpnLocator.ClientReleasesUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    /* ignore */
                }
            }
        }
        finally
        {
            MellivoraAmneziaDownloadButton.IsEnabled = true;
            SyncMellivoraWarpStatusText();
        }
    }

    private async void MellivoraMihomoFetchButton_Click(object sender, RoutedEventArgs e)
    {
        MellivoraMihomoFetchButton.IsEnabled = false;
        try
        {
            var progress = new Progress<string>(s => MellivoraWarpStatusText.Text = s);
            var (ok, err) = await MihomoBootstrapper.EnsureReadyAsync(
                    MellivoraWarpHost.WarpcfgDirectory,
                    forceRedownload: true,
                    progress,
                    CancellationToken.None)
                .ConfigureAwait(true);
            if (ok)
            {
                MessageBox.Show(this,
                    "Mihomo " + MihomoBootstrapper.PinnedVersion + " установлен в mellivoravpn\\warpcfg\\mihomo.exe",
                    "Дополнительные способы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(this,
                    err ?? "Ошибка загрузки.",
                    "Дополнительные способы",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            MellivoraMihomoFetchButton.IsEnabled = true;
            SyncMellivoraWarpStatusText();
        }
    }

    private void MellivoraAmneziaLaunchButton_Click(object sender, RoutedEventArgs e)
    {
        var exe = AmneziaVpnLocator.FindExecutable();
        if (exe != null)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Дополнительные способы", MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AmneziaVpnLocator.ClientReleasesUrl,
                UseShellExecute = true
            });
        }
        catch
        {
            /* ignore */
        }

        MessageBox.Show(this,
            "Amnezia VPN не найдена в стандартных путях. Открыта страница релизов на GitHub.",
            "Дополнительные способы",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
