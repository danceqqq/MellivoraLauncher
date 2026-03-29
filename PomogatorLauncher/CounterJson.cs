using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace PomogatorLauncher;

internal static class CommunityCounterUrls
{
    internal const string Discord =
        "https://raw.githubusercontent.com/danceqqq/MellivoraLauncher/main/counter/discord.json";

    internal const string Family =
        "https://raw.githubusercontent.com/danceqqq/MellivoraLauncher/main/counter/family.json";
}

internal static class CounterJson
{
    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(18) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("MellivoraLauncher/1.0 (WPF)");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    /// <summary>Сначала GitHub raw, при сбое или невалидном JSON — локальный файл рядом с exe.</summary>
    public static async Task<string> FetchDisplayOrFallbackAsync(string url, string localPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await Http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
            var remote = ParseDisplayFromJson(json);
            if (remote != "—") return remote;
        }
        catch
        {
            /* сеть / таймаут / не 200 */
        }

        return ReadDisplay(localPath);
    }

    /// <summary>Читает отображаемое значение: приоритет <c>value</c>, затем <c>online</c>, затем <c>count</c>.</summary>
    public static string ReadDisplay(string path)
    {
        try
        {
            if (!File.Exists(path)) return "—";
            return ParseDisplayFromJson(File.ReadAllText(path));
        }
        catch
        {
            return "—";
        }
    }

    public static string ParseDisplayFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return "—";

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var valueEl))
            {
                if (valueEl.ValueKind == JsonValueKind.String)
                {
                    var s = valueEl.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!;
                }

                if (valueEl.ValueKind == JsonValueKind.Number)
                    return FormatNumber(valueEl);
            }

            if (root.TryGetProperty("online", out var onlineEl))
            {
                if (onlineEl.ValueKind == JsonValueKind.String)
                {
                    var s = onlineEl.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!;
                }

                if (onlineEl.ValueKind == JsonValueKind.Number)
                    return FormatNumber(onlineEl);
            }

            if (root.TryGetProperty("count", out var countEl) && countEl.ValueKind == JsonValueKind.Number)
                return FormatNumber(countEl);
        }
        catch
        {
            /* битый JSON */
        }

        return "—";
    }

    private static string FormatNumber(JsonElement el)
    {
        if (el.TryGetInt32(out var i)) return i.ToString();
        if (el.TryGetInt64(out var l)) return l.ToString();
        return el.GetRawText();
    }
}
