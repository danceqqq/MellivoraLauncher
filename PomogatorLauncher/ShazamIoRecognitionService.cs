using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace PomogatorLauncher;

/// <summary>
/// ShazamIO (<see href="https://github.com/shazamio/ShazamIO"/>): WAV в stdin процесса Python, без AudD.
/// </summary>
internal static class ShazamIoRecognitionService
{
    internal const string NoMatchMarker = "no_match";

    internal static async Task<SongRecognitionResult> RecognizeWavBytesAsync(byte[] wav, CancellationToken ct)
    {
        if (wav.Length < 256)
            return SongRecognitionResult.Fail("Слишком мало аудио для распознавания.");

        var script = Path.Combine(AppContext.BaseDirectory, "Assets", "ShazamIO", "recognize_stdin.py");
        if (!File.Exists(script))
            return SongRecognitionResult.Fail("Не найден Assets/ShazamIO/recognize_stdin.py рядом с приложением.");

        var pythonExe = PythonLocator.FindPythonExecutable();
        if (pythonExe == null)
            return SongRecognitionResult.Fail(
                "Не найден Python. Ожидается встроенный интерпретатор в Assets\\ShazamIO\\py-embed (пересоберите проект с интернетом) или укажите python.exe в shazam_settings.json → PythonExecutable.");

        var isPyLauncher = string.Equals(Path.GetFileNameWithoutExtension(pythonExe), "py",
            StringComparison.OrdinalIgnoreCase);
        var args = isPyLauncher
            ? $"-3 \"{script}\""
            : $"\"{script}\"";

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };
        psi.Environment["PYTHONUTF8"] = "1";
        psi.Environment["PYTHONIOENCODING"] = "utf-8";

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!proc.Start())
            return SongRecognitionResult.Fail("Не удалось запустить Python.");

        using (ct.Register(() =>
               {
                   try
                   {
                       if (!proc.HasExited)
                           proc.Kill(true);
                   }
                   catch
                   {
                       /* ignore */
                   }
               }))
        {
            try
            {
                await proc.StandardInput.BaseStream.WriteAsync(wav, 0, wav.Length, ct).ConfigureAwait(false);
                await proc.StandardInput.BaseStream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return SongRecognitionResult.Fail("Запись в stdin Python: " + ex.Message);
            }
            finally
            {
                try
                {
                    proc.StandardInput.Close();
                }
                catch
                {
                    /* ignore */
                }
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = proc.StandardError.ReadToEndAsync(ct);
            try
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (proc.ExitCode == 3)
                return SongRecognitionResult.Fail(
                    "Нет пакета shazamio. Выполните: pip install shazamio (см. https://github.com/shazamio/ShazamIO )");

            var jsonLine = ExtractJsonLine(stdout);
            if (string.IsNullOrWhiteSpace(jsonLine))
            {
                var hint = string.IsNullOrWhiteSpace(stderr) ? "Пустой ответ Python." : stderr.Trim();
                if (hint.Contains("No module named", StringComparison.OrdinalIgnoreCase)
                    || hint.Contains("shazamio", StringComparison.OrdinalIgnoreCase))
                    return SongRecognitionResult.Fail("Установите: pip install shazamio");

                return SongRecognitionResult.Fail(string.IsNullOrWhiteSpace(hint) ? "Нет JSON от ShazamIO." : hint);
            }

            return ParseCliJson(jsonLine);
        }
    }

    private static string? ExtractJsonLine(string stdout)
    {
        var lines = stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var t = lines[i].Trim();
            if (t.Length > 0 && t[0] == '{')
                return t;
        }

        var s = stdout.Trim();
        return s.Length > 0 && s[0] == '{' ? s : null;
    }

    private static SongRecognitionResult ParseCliJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            if (!ok)
            {
                var err = root.TryGetProperty("error", out var e) && e.ValueKind == JsonValueKind.String
                    ? e.GetString()
                    : "Ошибка ShazamIO.";
                if (string.Equals(err, NoMatchMarker, StringComparison.Ordinal))
                    return SongRecognitionResult.Fail(NoMatchMarker);
                if (err != null && err.Contains("pip install shazamio", StringComparison.OrdinalIgnoreCase))
                    return SongRecognitionResult.Fail(err);
                return SongRecognitionResult.Fail(err ?? "Ошибка ShazamIO.");
            }

            var title = root.TryGetProperty("title", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? ""
                : "";
            var artist = root.TryGetProperty("artist", out var a) && a.ValueKind == JsonValueKind.String
                ? a.GetString() ?? ""
                : "";
            var cover = root.TryGetProperty("coverUrl", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;

            return SongRecognitionResult.Success(
                string.IsNullOrWhiteSpace(title) ? "Неизвестный трек" : title.Trim(),
                string.IsNullOrWhiteSpace(artist) ? "Неизвестный исполнитель" : artist.Trim(),
                cover);
        }
        catch
        {
            return SongRecognitionResult.Fail("Не удалось разобрать ответ ShazamIO.");
        }
    }
}

internal static class PythonLocator
{
    internal static string? FindPythonExecutable()
    {
        var configured = ShazamSettingsStore.LoadPythonExecutable();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            var p = configured.Trim();
            if (File.Exists(p))
                return p;
        }

        var bundled = Path.Combine(AppContext.BaseDirectory, "Assets", "ShazamIO", "py-embed", "python.exe");
        if (File.Exists(bundled))
            return bundled;

        foreach (var name in new[] { "py.exe", "python.exe", "python3.exe" })
        {
            var found = FindOnPath(name);
            if (found != null)
                return found;
        }

        foreach (var name in new[] { "py", "python", "python3" })
        {
            var found = FindOnPath(name);
            if (found != null)
                return found;
        }

        return null;
    }

    private static string? FindOnPath(string fileName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
            return null;

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(full))
                    return full;
            }
            catch
            {
                /* ignore */
            }
        }

        return null;
    }
}
