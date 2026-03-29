using System.Diagnostics;
using System.IO;
using System.Text;

namespace PomogatorLauncher;

/// <summary>
/// Запуск Mihomo/Clash Meta для YAML из <c>mellivoravpn\warpcfg</c> (положите mihomo.exe рядом с конфигами).
/// </summary>
internal static class MellivoraWarpHost
{
    internal const string MihomoReleasesUrl = "https://github.com/MetaCubeX/mihomo/releases/tag/v1.19.21";

    private static readonly string[] MihomoExeNames =
    [
        "mihomo.exe",
        "Mihomo.exe",
        "clash-meta.exe",
        "Clash.Meta.exe"
    ];

    private static Process? _startedByUs;

    internal static string WarpcfgDirectory =>
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "mellivoravpn", "warpcfg"));

    internal static string AppliedWorkDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PomogatorLauncher",
            "MellivoraWarp");

    internal static string RuntimeConfigPath => Path.Combine(AppliedWorkDirectory, "runtime.yaml");

    internal static IEnumerable<string> EnumerateConfigFiles()
    {
        var dir = WarpcfgDirectory;
        if (!Directory.Exists(dir))
            yield break;

        string[] patterns = ["*.yaml", "*.yml", "*.conf"];
        foreach (var pattern in patterns)
        {
            string[] files;
            try
            {
                files = Directory.GetFiles(dir, pattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var f in files.OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase))
                yield return f;
        }
    }

    internal static string? FindMihomoExecutable()
    {
        var dir = WarpcfgDirectory;
        if (Directory.Exists(dir))
        {
            foreach (var name in MihomoExeNames)
            {
                var full = Path.Combine(dir, name);
                if (File.Exists(full))
                    return Path.GetFullPath(full);
            }

            try
            {
                foreach (var full in Directory.EnumerateFiles(dir, "mihomo*.exe", SearchOption.TopDirectoryOnly))
                {
                    if (File.Exists(full))
                        return Path.GetFullPath(full);
                }
            }
            catch
            {
                /* ignore */
            }
        }

        var baseDir = AppContext.BaseDirectory;
        foreach (var name in MihomoExeNames)
        {
            var full = Path.Combine(baseDir, name);
            if (File.Exists(full))
                return Path.GetFullPath(full);
        }

        try
        {
            foreach (var full in Directory.EnumerateFiles(baseDir, "mihomo*.exe", SearchOption.TopDirectoryOnly))
            {
                if (File.Exists(full))
                    return Path.GetFullPath(full);
            }
        }
        catch
        {
            /* ignore */
        }

        return null;
    }

    /// <summary>Собирает рабочий runtime.yaml: порты, правило MATCH → группа WARP.</summary>
    internal static void WriteRuntimeYamlFromSource(string sourceYamlPath, string destPath)
    {
        var user = File.ReadAllText(sourceYamlPath, Encoding.UTF8);
        var sb = new StringBuilder();
        sb.AppendLine("# PomogatorLauncher — префикс для Mihomo (порты и rules)");
        sb.AppendLine("mixed-port: 20808");
        sb.AppendLine("socks-port: 20809");
        sb.AppendLine("external-controller: 127.0.0.1:20909");
        sb.AppendLine("allow-lan: false");
        sb.AppendLine("ipv6: true");
        sb.AppendLine("mode: rule");
        sb.AppendLine("log-level: warning");
        sb.AppendLine();
        sb.AppendLine(user.Trim());
        if (!user.Contains("rules:", StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine();
            sb.AppendLine("rules:");
            sb.AppendLine("  - MATCH, WARP");
        }

        var d = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(d))
            Directory.CreateDirectory(d);
        File.WriteAllText(destPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    internal static void CopyConfToApplied(string sourceConfPath, string destFileName)
    {
        var dir = AppliedWorkDirectory;
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, destFileName);
        File.Copy(sourceConfPath, dest, overwrite: true);
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

    internal static bool TryStartMihomo(string runtimeYamlPath, out string? error)
    {
        error = null;
        StopIfStartedByUs();

        var exe = FindMihomoExecutable();
        if (string.IsNullOrEmpty(exe))
        {
            error =
                "Не найден mihomo.exe. Скачайте Mihomo (Clash Meta) и положите mihomo.exe в папку mellivoravpn\\warpcfg рядом с конфигами.";
            return false;
        }

        var workDir = AppliedWorkDirectory;
        Directory.CreateDirectory(workDir);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"-f \"{runtimeYamlPath}\" -d \"{workDir}\"",
                WorkingDirectory = workDir,
                UseShellExecute = false,
                CreateNoWindow = true
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
