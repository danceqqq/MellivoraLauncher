using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace PomogatorLauncher;

/// <summary>Онлайн Detroit со страницы Majestic Wiki (JSON в HTML + запасной разбор карточки).</summary>
internal static class FletcherMonitoring
{
    public const string MonitoringUrl = "https://wiki.majestic-rp.ru/ru/servers";

    public static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        c.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml;q=0.9,*/*;q=0.8");
        c.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en;q=0.8");
        return c;
    }

    private static string NormalizeCount(string t) =>
        t.Replace('\u00A0', ' ').Trim();

    private static bool LooksLikePlayerCountText(string t)
    {
        t = NormalizeCount(t);
        if (string.IsNullOrEmpty(t)) return false;
        return Regex.IsMatch(t, @"^[\d\s]+$") && Regex.IsMatch(t, @"\d");
    }

    /// <summary>Группы по 3 разряда с пробелом: 2156 → "2 156".</summary>
    public static string FormatPlayersRu(int value)
    {
        var s = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        if (s.Length <= 3) return s;
        var rem = s.Length % 3;
        if (rem == 0) rem = 3;
        var sb = new StringBuilder();
        sb.Append(s.AsSpan(0, rem));
        for (var i = rem; i < s.Length; i += 3)
        {
            sb.Append(' ');
            sb.Append(s.AsSpan(i, 3));
        }

        return sb.ToString();
    }

    /// <summary>
    /// В payload страницы: \"name\":\"Detroit\" … \"players\":2156 … \"status\":true
    /// (в видимой карточке часто &lt;span&gt;0&lt;/span&gt; до гидрации).
    /// </summary>
    public static bool TryParseDetroitFromJson(string html, out string countText, out bool online)
    {
        countText = "";
        online = false;
        if (string.IsNullOrEmpty(html)) return false;

        // В теле страницы: Detroit\",\"branch\":\"release\",…\"players\":2156,…\"status\":true
        var m = Regex.Match(
            html,
            @"Detroit\\"",\\""branch\\""[\s\S]{0,4000}?\\""players\\"":(\d+)[\s\S]{0,800}?\\""status\\"":(true|false)",
            RegexOptions.CultureInvariant);

        if (!m.Success)
            return false;

        if (!int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var players))
            return false;

        countText = FormatPlayersRu(players);
        online = m.Groups[2].Value.Equals("true", StringComparison.Ordinal);
        return true;
    }

    /// <summary>Карточка SSR: span + «онлайн» (часто 0).</summary>
    public static string? TryParseDetroitSpanCard(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;

        var idx = html.IndexOf("Detroit", StringComparison.Ordinal);
        if (idx < 0) return null;

        var len = Math.Min(4500, html.Length - idx);
        if (len <= 0) return null;
        var window = html.AsSpan(idx, len).ToString();

        var m = Regex.Match(
            window,
            @"<span[^>]*>([\d\s\u00A0]+)</span>[\s\S]{0,320}?онлайн",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        if (m.Success && LooksLikePlayerCountText(m.Groups[1].Value))
            return NormalizeCount(m.Groups[1].Value);

        m = Regex.Match(window, @"<span[^>]*>([\d\s\u00A0]+)</span>", RegexOptions.IgnoreCase);
        if (m.Success && LooksLikePlayerCountText(m.Groups[1].Value))
            return NormalizeCount(m.Groups[1].Value);

        return null;
    }

    public static string? TryParsePlayerCount(string html)
    {
        if (TryParseDetroitFromJson(html, out var jsonCount, out _))
            return jsonCount;
        return TryParseDetroitSpanCard(html);
    }

    public static bool ResolveServerOnline(string html)
    {
        if (string.IsNullOrEmpty(html)) return false;
        if (IsLikelyChallengeOrBlockPage(html)) return false;
        if (IsJavaScriptAntiBotShell(html)) return false;

        if (TryParseDetroitFromJson(html, out _, out var online))
            return online;

        if (TryParseDetroitSpanCard(html) != null)
            return true;

        return Regex.IsMatch(
            html,
            @"Detroit[\s\S]{0,4500}?онлайн",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    public static bool IsLikelyChallengeOrBlockPage(string html) =>
        html.Contains("DDoS protection", StringComparison.OrdinalIgnoreCase)
        || html.Contains("Processing your request", StringComparison.OrdinalIgnoreCase)
        || html.Contains("Check your browser", StringComparison.OrdinalIgnoreCase);

    public static bool IsJavaScriptAntiBotShell(string html) =>
        !string.IsNullOrEmpty(html)
        && html.Length < 6000
        && (html.Contains("slowAES", StringComparison.Ordinal)
            || html.Contains("vddosw3data", StringComparison.OrdinalIgnoreCase)
            || html.Contains("w3IncludeHTML", StringComparison.Ordinal));
}
