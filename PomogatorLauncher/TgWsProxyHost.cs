using System.Diagnostics;
using System.IO;

namespace PomogatorLauncher;

/// <summary>
/// Локальный TG WS Proxy (<see href="https://github.com/Flowseal/tg-ws-proxy"/>): SOCKS5 на 127.0.0.1:1080.
/// Ожидается готовый <c>TgWsProxy_windows.exe</c> рядом с приложением (скачать с GitHub Releases).
/// </summary>
internal static class TgWsProxyHost
{
    internal const string ReleasesUrl = "https://github.com/Flowseal/tg-ws-proxy/releases";
    internal const string Socks5Loopback = "socks5://127.0.0.1:1080";

    private static readonly string[] PreferredExeNames =
    [
        "TgWsProxy_windows.exe",
        "TgWsProxy_windows_7_64bit.exe"
    ];

    private static Process? _startedByUs;

    private static IEnumerable<string> SearchRoots()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "mellivoravpn", "tg_ws_proxy");
        yield return baseDir;
        yield return Path.Combine(baseDir, "mellivoravpn");
    }

    internal static string? FindBundledExecutable()
    {
        foreach (var root in SearchRoots())
        {
            if (!Directory.Exists(root))
                continue;
            foreach (var name in PreferredExeNames)
            {
                var full = Path.Combine(root, name);
                if (File.Exists(full))
                    return Path.GetFullPath(full);
            }
        }

        var enumOpts = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System
        };

        foreach (var root in SearchRoots())
        {
            if (!Directory.Exists(root))
                continue;
            try
            {
                var hit = Directory.EnumerateFiles(root, "TgWsProxy*.exe", enumOpts).FirstOrDefault();
                if (hit != null && File.Exists(hit))
                    return Path.GetFullPath(hit);
            }
            catch
            {
                /* ignore */
            }
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

    /// <summary>Запускает exe, если найден. Возвращает false, если файла нет.</summary>
    internal static bool TryStart(out string? error)
    {
        error = null;
        if (IsOurInstanceRunning())
            return true;

        var exe = FindBundledExecutable();
        if (string.IsNullOrEmpty(exe))
        {
            error = "Не найден TgWsProxy_windows.exe. Скачайте сборку с GitHub Releases и положите exe в папку mellivoravpn\\tg_ws_proxy рядом с приложением.";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            };
            var p = Process.Start(psi);
            if (p == null)
            {
                error = "Не удалось запустить процесс.";
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
