using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;

namespace PomogatorLauncher;

/// <summary>
/// Скачивает Mihomo (Clash Meta) с <see href="https://github.com/MetaCubeX/mihomo/releases/tag/v1.19.21">релиза v1.19.21</see>
/// в <c>mellivoravpn\warpcfg\mihomo.exe</c>. В zip файл называется <c>mihomo-windows-amd64-v3.exe</c> и т.п., не <c>mihomo.exe</c>.
/// </summary>
internal static class MihomoBootstrapper
{
    internal const string PinnedVersion = "v1.19.21";

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Порядок: предпочитаем v3 (amd64), затем запасные варианты из того же тега.</summary>
    private static IReadOnlyList<string> WindowsAmd64ZipCandidates =>
    [
        "mihomo-windows-amd64-v3-v1.19.21.zip",
        "mihomo-windows-amd64-v3-go125-v1.19.21.zip",
        "mihomo-windows-amd64-v1-v1.19.21.zip",
        "mihomo-windows-amd64-v1.19.21.zip"
    ];

    private static IEnumerable<string> ZipCandidatesToTry()
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            yield return "mihomo-windows-arm64-v1.19.21.zip";
            yield break;
        }

        if (RuntimeInformation.ProcessArchitecture == Architecture.X86)
        {
            yield return "mihomo-windows-386-v1.19.21.zip";
            yield break;
        }

        foreach (var z in WindowsAmd64ZipCandidates)
            yield return z;
    }

    private static string ReleaseDownloadBase =>
        $"https://github.com/MetaCubeX/mihomo/releases/download/{PinnedVersion}/";

    internal static string VersionStampPath(string warpcfgDir) =>
        Path.Combine(warpcfgDir, ".pomogator_mihomo_version");

    internal static string TargetExePath(string warpcfgDir) =>
        Path.Combine(warpcfgDir, "mihomo.exe");

    internal static async Task<(bool Ok, string? Error)> EnsureReadyAsync(
        string warpcfgDir,
        bool forceRedownload,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        await Gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(warpcfgDir);
            var exePath = TargetExePath(warpcfgDir);
            var verPath = VersionStampPath(warpcfgDir);

            if (forceRedownload)
            {
                progress?.Report("Удаляю старый mihomo.exe…");
                TryDelete(exePath);
                TryDelete(verPath);
            }

            if (File.Exists(exePath))
            {
                if (!File.Exists(verPath))
                    return (true, null);

                try
                {
                    var v = (await File.ReadAllTextAsync(verPath, ct).ConfigureAwait(false)).Trim();
                    if (string.Equals(v, PinnedVersion, StringComparison.Ordinal))
                        return (true, null);
                }
                catch
                {
                    return (true, null);
                }

                progress?.Report("Обновляю Mihomo до " + PinnedVersion + "…");
                TryDelete(exePath);
                TryDelete(verPath);
            }

            using var client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("PomogatorLauncher/1.0 (Windows; .NET)");
            client.Timeout = TimeSpan.FromMinutes(8);

            Exception? lastEx = null;
            foreach (var zipName in ZipCandidatesToTry())
            {
                var url = ReleaseDownloadBase + zipName;
                progress?.Report("Скачиваю Mihomo " + PinnedVersion + " (" + zipName + ")…");
                var tmpZip = Path.Combine(Path.GetTempPath(), "pomogator_mihomo_" + Guid.NewGuid().ToString("N") + ".zip");
                try
                {
                    using (var resp = await client.GetAsync(new Uri(url), HttpCompletionOption.ResponseHeadersRead, ct)
                                       .ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            lastEx = new HttpRequestException(resp.StatusCode + " для " + zipName);
                            continue;
                        }

                        await using (var dl = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                        await using (var fs = new FileStream(tmpZip, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            await dl.CopyToAsync(fs, ct).ConfigureAwait(false);
                    }

                    using var zip = ZipFile.OpenRead(tmpZip);
                    var exeEntry = PickMihomoBinaryEntry(zip);
                    if (exeEntry == null)
                    {
                        var list = string.Join(", ", zip.Entries.Select(e => e.FullName));
                        lastEx = new InvalidOperationException("В " + zipName + " нет подходящего .exe. Записи: " + list);
                        continue;
                    }

                    progress?.Report("Распаковка → mihomo.exe…");
                    var tmpExe = exePath + ".tmp";
                    TryDelete(tmpExe);
                    await using (var fs = new FileStream(tmpExe, FileMode.Create, FileAccess.Write, FileShare.None))
                    await using (var es = exeEntry.Open())
                    {
                        await es.CopyToAsync(fs, ct).ConfigureAwait(false);
                    }

                    if (File.Exists(exePath))
                        TryDelete(exePath);
                    File.Move(tmpExe, exePath);

                    await File.WriteAllTextAsync(verPath, PinnedVersion + Environment.NewLine, ct).ConfigureAwait(false);
                    progress?.Report("Mihomo " + PinnedVersion + " готов (" + zipName + ").");
                    return (true, null);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                }
                finally
                {
                    TryDelete(tmpZip);
                }
            }

            return (false, lastEx?.Message ?? "Не удалось скачать ни один архив Mihomo.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>Ищет единственный подходящий exe в архиве (имя содержит mihomo или clash-meta; иначе единственный .exe в корне).</summary>
    private static ZipArchiveEntry? PickMihomoBinaryEntry(ZipArchive zip)
    {
        var exeEntries = zip.Entries
            .Where(e => !string.IsNullOrEmpty(e.Name) &&
                        e.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                        e.FullName.Replace('\\', '/').IndexOf('/') < 0)
            .ToList();

        foreach (var e in exeEntries)
        {
            var n = e.Name;
            if (n.Contains("mihomo", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("clash-meta", StringComparison.OrdinalIgnoreCase) ||
                n.Contains("Clash.Meta", StringComparison.OrdinalIgnoreCase))
                return e;
        }

        return exeEntries.Count == 1 ? exeEntries[0] : null;
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
