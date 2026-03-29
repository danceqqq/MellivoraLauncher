using System.IO;
using System.Text.Json;

namespace PomogatorLauncher;

/// <summary>
/// Опционально: полный путь к python.exe в shazam_settings.json → PythonExecutable.
/// </summary>
internal static class ShazamSettingsStore
{
    private static readonly JsonSerializerOptions JsonOpt = new() { WriteIndented = true };

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PomogatorLauncher",
            "shazam_settings.json");

    internal static string? LoadPythonExecutable()
    {
        try
        {
            EnsureSettingsFileExists();
            var dto = JsonSerializer.Deserialize<ShazamSettingsDto>(File.ReadAllText(SettingsPath));
            return string.IsNullOrWhiteSpace(dto?.PythonExecutable) ? null : dto.PythonExecutable.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureSettingsFileExists()
    {
        var path = SettingsPath;
        if (File.Exists(path)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var initial = new ShazamSettingsDto { PythonExecutable = "" };
        File.WriteAllText(path, JsonSerializer.Serialize(initial, JsonOpt));
    }

    private sealed class ShazamSettingsDto
    {
        public string? PythonExecutable { get; set; }
    }
}
