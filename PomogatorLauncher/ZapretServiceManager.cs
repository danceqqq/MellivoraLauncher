using System.Diagnostics;
using System.IO;
using System.Text;

namespace PomogatorLauncher;

/// <summary>
/// Установка/удаление службы zapret по логике mellivoravpn\zapret_telegram\service.bat
/// (профиль general (ALT).bat, без запуска service.bat).
/// </summary>
internal static class ZapretServiceManager
{
    internal const string GeneralAltBatFileName = "general (ALT).bat";
    private const string ServiceName = "zapret";

    internal static bool IsZapretServiceInstalled()
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"query {ServiceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (p == null) return false;
            p.WaitForExit(15_000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    internal static void EnsureUserLists(string zapretRoot)
    {
        var lists = Path.Combine(zapretRoot, "lists");
        try
        {
            Directory.CreateDirectory(lists);
            EnsureFile(Path.Combine(lists, "ipset-exclude-user.txt"), "203.0.113.113/32" + Environment.NewLine);
            EnsureFile(Path.Combine(lists, "list-general-user.txt"), "domain.example.abc" + Environment.NewLine);
            EnsureFile(Path.Combine(lists, "list-exclude-user.txt"), "domain.example.abc" + Environment.NewLine);
        }
        catch
        {
            /* ignore */
        }
    }

    private static void EnsureFile(string path, string defaultContent)
    {
        if (!File.Exists(path))
            File.WriteAllText(path, defaultContent);
    }

    /// <summary>Как service.bat :game_switch_status (порты для %%GameFilter*%%).</summary>
    internal static (string Tcp, string Udp) ReadGameFilterPorts(string zapretRoot)
    {
        var flag = Path.Combine(zapretRoot, "utils", "game_filter.enabled");
        if (!File.Exists(flag))
            return ("12", "12");
        try
        {
            var mode = File.ReadAllText(flag).Trim().ToLowerInvariant();
            return mode switch
            {
                "all" => ("1024-65535", "1024-65535"),
                "tcp" => ("1024-65535", "12"),
                "udp" => ("12", "1024-65535"),
                _ => ("12", "12")
            };
        }
        catch
        {
            return ("12", "12");
        }
    }

    internal static bool TryBuildWinwsServiceArguments(string zapretRoot, out string winwsFullPath, out string argumentLine, out string? error)
    {
        winwsFullPath = Path.GetFullPath(Path.Combine(zapretRoot, "bin", "winws.exe"));
        argumentLine = "";
        if (!File.Exists(winwsFullPath))
        {
            error =
                "Нет bin\\winws.exe в папке zapret-telegram. Нужен полный архив релиза с WinDivert.";
            return false;
        }

        var batPath = Path.Combine(zapretRoot, GeneralAltBatFileName);
        if (!File.Exists(batPath))
        {
            error = $"В папке zapret не найден файл «{GeneralAltBatFileName}».";
            return false;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(batPath);
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }

        var start = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("winws.exe", StringComparison.OrdinalIgnoreCase))
            {
                start = i;
                break;
            }
        }

        if (start < 0)
        {
            error = "В general (ALT).bat нет строки с winws.exe.";
            return false;
        }

        var binPath = Path.GetFullPath(Path.Combine(zapretRoot, "bin")) + Path.DirectorySeparatorChar;
        var listsPath = Path.GetFullPath(Path.Combine(zapretRoot, "lists")) + Path.DirectorySeparatorChar;
        var (gfTcp, gfUdp) = ReadGameFilterPorts(zapretRoot);
        var gfBoth = gfTcp == "1024-65535" || gfUdp == "1024-65535" ? "1024-65535" : "12";

        var sb = new StringBuilder();
        for (var i = start; i < lines.Length; i++)
        {
            var ln = lines[i].TrimEnd();
            if (ln.EndsWith('^'))
                ln = ln[..^1].TrimEnd();
            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append(ln);
        }

        var merged = sb.ToString()
            .Replace("%BIN%", binPath, StringComparison.OrdinalIgnoreCase)
            .Replace("%LISTS%", listsPath, StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilterTCP%", gfTcp, StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilterUDP%", gfUdp, StringComparison.OrdinalIgnoreCase)
            .Replace("%GameFilter%", gfBoth, StringComparison.OrdinalIgnoreCase);

        const StringComparison ign = StringComparison.OrdinalIgnoreCase;
        var winExeQuoted = "winws.exe\"";
        var idx = merged.IndexOf(winExeQuoted, ign);
        if (idx >= 0)
        {
            argumentLine = merged[(idx + winExeQuoted.Length)..].TrimStart();
        }
        else
        {
            var winExe = "winws.exe";
            idx = merged.IndexOf(winExe, ign);
            if (idx < 0)
            {
                error = "Не удалось выделить аргументы после winws.exe.";
                return false;
            }

            idx += winExe.Length;
            if (idx < merged.Length && merged[idx] == '"')
                idx++;
            argumentLine = merged[idx..].TrimStart();
        }

        if (string.IsNullOrWhiteSpace(argumentLine))
        {
            error = "Пустая командная строка winws после разбора bat.";
            return false;
        }

        error = null;
        return true;
    }

    internal static bool TryInstallZapretService(string zapretRoot, out string? error)
    {
        error = null;
        if (!TryBuildWinwsServiceArguments(zapretRoot, out var winws, out var args, out var buildErr))
        {
            error = buildErr;
            return false;
        }

        EnsureUserLists(zapretRoot);

        var ps1 = WriteInstallScript(winws, args);
        if (ps1 == null)
        {
            error = "Не удалось подготовить скрипт установки.";
            return false;
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\"",
                Verb = "runas",
                UseShellExecute = true
            });
            if (p == null)
            {
                error = "Не удалось запросить права администратора (UAC).";
                return false;
            }

            p.WaitForExit(120_000);
            try
            {
                File.Delete(ps1);
            }
            catch
            {
                /* ignore */
            }

            if (p.ExitCode != 0)
            {
                error = $"Установка завершилась с кодом {p.ExitCode}. Нужны права администратора и отсутствие конфликтующих служб.";
                return false;
            }

            if (!IsZapretServiceInstalled())
            {
                error = "Служба zapret не найдена после установки. Проверьте вывод sc и конфликтующие обходы.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? WriteInstallScript(string winwsFullPath, string argumentLine)
    {
        try
        {
            var exeB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(winwsFullPath));
            var argsB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(argumentLine));
            var path = Path.Combine(Path.GetTempPath(), "PomogatorZapretInstall_" + Guid.NewGuid().ToString("N") + ".ps1");
            var nl = Environment.NewLine;
            // sc.exe через Start-Process ломается: нужен один аргумент "binPath= …" с пробелом после =.
            // New-Service -BinaryPathName задаёт путь к exe и параметры так же, как диспетчер служб.
            var script =
                "$ErrorActionPreference = 'Stop'" + nl +
                "$tcp = netsh interface tcp show global 2>$null | Out-String" + nl +
                "if ($tcp -notmatch '(?i)timestamps\\s+.*enabled') { netsh interface tcp set global timestamps=enabled | Out-Null }" + nl +
                "$exe = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + exeB64 + "'))" + nl +
                "$argLine = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + argsB64 + "'))" + nl +
                "Stop-Service -Name 'zapret' -Force -ErrorAction SilentlyContinue" + nl +
                "Start-Sleep -Milliseconds 500" + nl +
                "& sc.exe delete zapret 2>$null | Out-Null" + nl +
                "Start-Sleep -Milliseconds 500" + nl +
                "$bpn = [char]34 + $exe + [char]34 + ' ' + $argLine" + nl +
                "New-Service -Name 'zapret' -BinaryPathName $bpn -DisplayName 'zapret' -StartupType Automatic" + nl +
                "Set-Service -Name 'zapret' -Description 'Zapret DPI bypass software' -ErrorAction SilentlyContinue" + nl +
                "Start-Service -Name 'zapret' -ErrorAction Stop" + nl +
                "Start-Process -FilePath reg.exe -ArgumentList @('add','HKLM\\System\\CurrentControlSet\\Services\\zapret','/v','zapret-discord-youtube','/t','REG_SZ','/d','general (ALT)','/f') -Wait -NoNewWindow" + nl +
                "exit 0" + nl;
            File.WriteAllText(path, script, new UTF8Encoding(false));
            return path;
        }
        catch
        {
            return null;
        }
    }

    internal static bool TryRemoveZapretService(out string? error)
    {
        error = null;
        var ps1 = WriteRemoveScript();
        if (ps1 == null)
        {
            error = "Не удалось подготовить скрипт удаления.";
            return false;
        }

        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{ps1}\"",
                Verb = "runas",
                UseShellExecute = true
            });
            if (p == null)
            {
                error = "Не удалось запросить права администратора (UAC).";
                return false;
            }

            p.WaitForExit(120_000);
            try
            {
                File.Delete(ps1);
            }
            catch
            {
                /* ignore */
            }

            if (p.ExitCode != 0)
            {
                error = $"Удаление завершилось с кодом {p.ExitCode}.";
                return false;
            }

            if (IsZapretServiceInstalled())
            {
                error = "Служба zapret всё ещё установлена. Закройте конфликтующие процессы и повторите.";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static string? WriteRemoveScript()
    {
        try
        {
            var path = Path.Combine(Path.GetTempPath(), "PomogatorZapretRemove_" + Guid.NewGuid().ToString("N") + ".ps1");
            const string script = """
$ErrorActionPreference = 'Continue'
$q = Start-Process -FilePath sc.exe -ArgumentList @('query','zapret') -Wait -PassThru -NoNewWindow
if ($q.ExitCode -eq 0) {
  Start-Process -FilePath net.exe -ArgumentList @('stop','zapret') -Wait -NoNewWindow | Out-Null
  Start-Process -FilePath sc.exe -ArgumentList @('delete','zapret') -Wait -NoNewWindow | Out-Null
}
Get-Process -Name winws -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
$w = Start-Process -FilePath sc.exe -ArgumentList @('query','WinDivert') -Wait -PassThru -NoNewWindow
if ($w.ExitCode -eq 0) {
  Start-Process -FilePath net.exe -ArgumentList @('stop','WinDivert') -Wait -NoNewWindow | Out-Null
  $w2 = Start-Process -FilePath sc.exe -ArgumentList @('query','WinDivert') -Wait -PassThru -NoNewWindow
  if ($w2.ExitCode -eq 0) { Start-Process -FilePath sc.exe -ArgumentList @('delete','WinDivert') -Wait -NoNewWindow | Out-Null }
}
Start-Process -FilePath net.exe -ArgumentList @('stop','WinDivert14') -Wait -NoNewWindow | Out-Null
Start-Process -FilePath sc.exe -ArgumentList @('delete','WinDivert14') -Wait -NoNewWindow | Out-Null
exit 0
""";
            File.WriteAllText(path, script, new UTF8Encoding(false));
            return path;
        }
        catch
        {
            return null;
        }
    }
}
