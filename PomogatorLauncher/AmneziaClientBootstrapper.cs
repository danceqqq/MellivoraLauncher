using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace PomogatorLauncher;

/// <summary>
/// Качает последний .exe с <see href="https://github.com/amnezia-vpn/amnezia-client/releases">релизов amnezia-client</see> и запускает установщик.
/// </summary>
internal static class AmneziaClientBootstrapper
{
    private const string LatestReleaseApi =
        "https://api.github.com/repos/amnezia-vpn/amnezia-client/releases/latest";

    internal static async Task<(bool Ok, string? Error)> DownloadLatestWindowsInstallerAndRunAsync(
        IProgress<string>? progress,
        CancellationToken ct)
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("PomogatorLauncher/1.0 (Windows; .NET)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.Timeout = TimeSpan.FromMinutes(15);

        progress?.Report("Запрос к GitHub: последний релиз Amnezia…");
        string json;
        try
        {
            json = await client.GetStringAsync(new Uri(LatestReleaseApi), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return (false, "Не удалось получить список релизов: " + ex.Message);
        }

        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return (false, "В ответе GitHub нет assets.");

        string? bestUrl = null;
        string? bestName = null;
        var bestScore = int.MinValue;

        foreach (var a in assets.EnumerateArray())
        {
            if (!a.TryGetProperty("name", out var nameEl) ||
                !a.TryGetProperty("browser_download_url", out var urlEl))
                continue;
            var name = nameEl.GetString();
            var url = urlEl.GetString();
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url))
                continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            var score = ScoreWindowsInstallerCandidate(name);
            if (score > bestScore)
            {
                bestScore = score;
                bestUrl = url;
                bestName = name;
            }
        }

        if (string.IsNullOrEmpty(bestUrl) || string.IsNullOrEmpty(bestName))
            return (false, "На странице релиза не найден ни один .exe (нужен установщик Windows).");

        progress?.Report("Скачиваю " + bestName + "…");
        var dest = Path.Combine(Path.GetTempPath(),
            "PomogatorLauncher_" + Guid.NewGuid().ToString("N") + "_" + SanitizeFileName(bestName));
        TryDelete(dest);

        try
        {
            using (var resp = await client.GetAsync(new Uri(bestUrl), HttpCompletionOption.ResponseHeadersRead, ct)
                               .ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                await using (var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await using (var net = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    await net.CopyToAsync(fs, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            TryDelete(dest);
            return (false, "Ошибка загрузки: " + ex.Message);
        }

        progress?.Report("Запуск установщика…");
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = dest,
                UseShellExecute = true,
                WorkingDirectory = Path.GetTempPath()
            });
        }
        catch (Exception ex)
        {
            return (false, "Файл сохранён: " + dest + Environment.NewLine + "Запуск не удался: " + ex.Message);
        }

        return (true, null);
    }

    private static int ScoreWindowsInstallerCandidate(string fileName)
    {
        var n = fileName.ToLowerInvariant();
        var s = 0;
        if (n.Contains("windows") || n.Contains("_win") || n.Contains("win_"))
            s += 20;
        if (n.Contains("x64") || n.Contains("win64") || n.Contains("amd64"))
            s += 10;
        if (n.Contains("linux") || n.Contains("macos") || n.Contains(".app") || n.Contains("android"))
            s -= 100;
        if (n.Contains("uninstall") || n.Contains("debug") || n.Contains("symbols"))
            s -= 50;
        return s;
    }

    private static string SanitizeFileName(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* ignore */
        }
    }
}
