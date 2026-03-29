using System.IO;
using System.Text.Json;

namespace PomogatorLauncher;

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions ReadOpt = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOpt = new() { WriteIndented = true };

    private static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PomogatorLauncher",
            "app_settings.json");

    internal const string DefaultGlobalHotkey = "Alt+W";

    private sealed class AppSettingsDto
    {
        public string GlobalHotkey { get; set; } = DefaultGlobalHotkey;

        /// <summary>none | tgWs | zapret</summary>
        public string? TelegramBypassMode { get; set; }

        /// <summary>Устаревший флаг; при отсутствии <see cref="TelegramBypassMode"/> трактуется как tgWs.</summary>
        public bool TgBypassTelegram { get; set; }

        /// <summary>Имя файла в mellivoravpn\warpcfg (без пути).</summary>
        public string? SelectedWarpCfgFileName { get; set; }
    }

    private static AppSettingsDto LoadDto()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettingsDto();
            return JsonSerializer.Deserialize<AppSettingsDto>(File.ReadAllText(FilePath), ReadOpt) ?? new AppSettingsDto();
        }
        catch
        {
            return new AppSettingsDto();
        }
    }

    private static void SaveDto(AppSettingsDto dto)
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(dto, WriteOpt));
        }
        catch
        {
            /* ignore */
        }
    }

    private static void EnsureExists()
    {
        if (File.Exists(FilePath)) return;
        var d = new AppSettingsDto();
        SaveDto(d);
    }

    internal static string LoadGlobalHotkey()
    {
        EnsureExists();
        var h = LoadDto().GlobalHotkey?.Trim();
        return string.IsNullOrEmpty(h) ? DefaultGlobalHotkey : h;
    }

    internal static void SaveGlobalHotkey(string hotkeyDisplay)
    {
        var d = LoadDto();
        d.GlobalHotkey = hotkeyDisplay.Trim();
        SaveDto(d);
    }

    internal static TelegramBypassMode LoadTelegramBypassMode()
    {
        try
        {
            EnsureExists();
            return ParseTelegramBypassMode(LoadDto());
        }
        catch
        {
            return TelegramBypassMode.None;
        }
    }

    internal static void SaveTelegramBypassMode(TelegramBypassMode mode)
    {
        var d = LoadDto();
        d.TelegramBypassMode = mode switch
        {
            TelegramBypassMode.TgWsProxy => "tgWs",
            TelegramBypassMode.ZapretTelegram => "zapret",
            _ => "none"
        };
        d.TgBypassTelegram = mode == TelegramBypassMode.TgWsProxy;
        SaveDto(d);
    }

    private static TelegramBypassMode ParseTelegramBypassMode(AppSettingsDto d)
    {
        var s = d.TelegramBypassMode?.Trim().ToLowerInvariant();
        if (s is "tgws" or "tg_ws" or "tgwsproxy")
            return TelegramBypassMode.TgWsProxy;
        if (s is "zapret" or "zapret_telegram" or "zapret-telegram")
            return TelegramBypassMode.ZapretTelegram;
        if (s == "none")
            return TelegramBypassMode.None;

        return TelegramBypassMode.None;
    }

    internal static string? LoadSelectedWarpCfgFileName()
    {
        try
        {
            EnsureExists();
            var n = LoadDto().SelectedWarpCfgFileName?.Trim();
            return string.IsNullOrEmpty(n) ? null : n;
        }
        catch
        {
            return null;
        }
    }

    internal static void SaveSelectedWarpCfgFileName(string? fileName)
    {
        var d = LoadDto();
        d.SelectedWarpCfgFileName = string.IsNullOrWhiteSpace(fileName) ? null : fileName.Trim();
        SaveDto(d);
    }
}
