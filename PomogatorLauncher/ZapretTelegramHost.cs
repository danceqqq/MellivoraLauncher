using System.Diagnostics;
using System.IO;

namespace PomogatorLauncher;

/// <summary>
/// Запуск обхода на базе <see href="https://github.com/IMROVICH/zapret-telegram"/> (general.bat + bin).
/// Работает на уровне системы (WinDivert); приложение не использует SOCKS5.
/// </summary>
internal static class ZapretTelegramHost
{
    internal const string RepoUrl = "https://github.com/IMROVICH/zapret-telegram";

    private static Process? _startedByUs;

    internal static string? FindZapretRoot()
    {
        var baseDir = AppContext.BaseDirectory;
        var direct = Path.Combine(baseDir, "mellivoravpn", "zapret_telegram");
        if (Directory.Exists(direct) && File.Exists(Path.Combine(direct, "general.bat")))
            return Path.GetFullPath(direct);

        try
        {
            var melliva = Path.Combine(baseDir, "mellivoravpn");
            if (!Directory.Exists(melliva)) return null;
            foreach (var sub in Directory.GetDirectories(melliva))
            {
                var bat = Path.Combine(sub, "general.bat");
                if (File.Exists(bat))
                {
                    var name = Path.GetFileName(sub);
                    if (name.Equals("zapret_telegram", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("zapret-telegram", StringComparison.OrdinalIgnoreCase))
                        return Path.GetFullPath(sub);
                }
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    internal static bool IsOurInstanceRunning()
    {
        try
        {
            return _startedByUs is { HasExited: false };
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryStartGeneralBat(out string? error)
    {
        error = null;
        if (IsOurInstanceRunning())
            return true;

        var root = FindZapretRoot();
        if (string.IsNullOrEmpty(root))
        {
            error =
                "Не найдена папка mellivoravpn\\zapret_telegram с файлом general.bat. Скачайте архив с GitHub и распакуйте туда.";
            return false;
        }

        var bat = Path.Combine(root, "general.bat");
        var winws = Path.Combine(root, "bin", "winws.exe");
        if (!File.Exists(winws))
        {
            error =
                "В mellivoravpn\\zapret_telegram нет папки bin с winws.exe. В репозитории на GitHub её нет — возьмите полный архив релиза zapret-telegram (там bin из zapret-win-bundle), см. README репозитория.";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = bat,
                WorkingDirectory = root,
                UseShellExecute = true
            };
            var p = Process.Start(psi);
            if (p == null)
            {
                error = "Не удалось запустить general.bat (возможно, нужны права администратора — запустите Помогатор от имени администратора или стартуйте general.bat вручную).";
                return false;
            }

            _startedByUs = p;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static void StopIfStartedByUs()
    {
        var p = _startedByUs;
        _startedByUs = null;
        if (p == null) return;
        try
        {
            if (!p.HasExited)
                p.Kill(true);
        }
        catch
        {
            /* ignore */
        }
        finally
        {
            try
            {
                p.Dispose();
            }
            catch
            {
                /* ignore */
            }
        }
    }
}
