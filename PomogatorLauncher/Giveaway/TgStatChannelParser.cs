using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace PomogatorLauncher.Giveaway;

/// <summary>
/// Лента канала с tgstat.ru: URL вида https://tgstat.ru/channel/@handle из web.telegram.org/k/#@handle в web.json.
/// </summary>
internal static class TgStatChannelParser
{
    private static readonly Regex HandleFromWebOpen = new(@"#(@[a-zA-Z0-9_]+)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    /// <summary>Открывающий тег div с id post-… (двойные или одинарные кавычки).</summary>
    private static readonly Regex PostIdDiv = new(
        @"<div[^>]*\bid\s*=\s*[""']post-\d+[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    /// <summary>Путь поста: /channel/@slug/123 — на сайте часто идёт суффикс /stat, /quotes и т.д.</summary>
    private static readonly Regex HrefChannelPost = new(
        @"href\s*=\s*[""'](?:https://tgstat\.ru)?(/channel/@[^/""']+/\d+)(?:/[^""']*)?[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SmallDate = new(@"<small>\s*([^<]+?)\s*</small>", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PostTextOpen = new(
        @"<div\s+class\s*=\s*[""']post-text[""'][^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AvatarChannels = new(
        @"src\s*=\s*[""'](//static\d*\.tgstat\.ru/channels/[^""']+)[""']",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>https://tgstat.ru/channel/@slug (slug с ведущим @).</summary>
    internal static string BuildChannelBaseUrl(string? webOpenUrl, string telegramUsernameNoAt)
    {
        var m = HandleFromWebOpen.Match(webOpenUrl ?? "");
        var handle = m.Success ? m.Groups[1].Value : "@" + telegramUsernameNoAt.Trim().TrimStart('@');
        return "https://tgstat.ru/channel/" + handle;
    }

    /// <summary>Аватар карточки канала: rounded-circle / box-160-280, приоритет _0/, затем og:image.</summary>
    internal static string? TryFindChannelAvatarUrl(string html)
    {
        if (string.IsNullOrEmpty(html))
            return null;

        static string? Norm(string? p)
        {
            if (string.IsNullOrWhiteSpace(p))
                return null;
            p = p.Trim();
            if (p.StartsWith("//", StringComparison.Ordinal))
                return "https:" + p;
            if (p.StartsWith("http://", StringComparison.Ordinal))
                return "https://" + p[7..];
            return p;
        }

        // og:image (иногда дублирует аватар канала)
        var og = Regex.Match(html,
            @"<meta[^>]+property\s*=\s*[""']og:image[""'][^>]+content\s*=\s*[""']([^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (!og.Success)
        {
            og = Regex.Match(html,
                @"<meta[^>]+content\s*=\s*[""']([^""']+)[""'][^>]+property\s*=\s*[""']og:image[""']",
                RegexOptions.IgnoreCase);
        }

        if (og.Success)
        {
            var u = Norm(og.Groups[1].Value);
            if (u != null && u.Contains("tgstat", StringComparison.OrdinalIgnoreCase) &&
                u.Contains("channels", StringComparison.OrdinalIgnoreCase))
                return u;
        }

        string? bestSrc = null;
        var bestScore = -1;
        foreach (Match im in Regex.Matches(html, @"<img\b[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = im.Value;
            if (!tag.Contains("tgstat", StringComparison.OrdinalIgnoreCase))
                continue;

            var srcM = Regex.Match(tag, @"\b(?:src|data-src)\s*=\s*[""']([^""']+)[""']", RegexOptions.IgnoreCase);
            if (!srcM.Success)
                continue;
            var src = srcM.Groups[1].Value;
            if (!src.Contains("channels", StringComparison.OrdinalIgnoreCase))
                continue;

            var score = 0;
            if (tag.Contains("rounded-circle", StringComparison.OrdinalIgnoreCase))
                score += 80;
            if (tag.Contains("box-160-280", StringComparison.OrdinalIgnoreCase))
                score += 60;
            if (src.Contains("/channels/_0/", StringComparison.OrdinalIgnoreCase))
                score += 40;
            else if (src.Contains("/channels/_50/", StringComparison.OrdinalIgnoreCase))
                score += 15;
            else if (Regex.IsMatch(src, @"/channels/_\d+/", RegexOptions.IgnoreCase))
                score += 25;

            if (score > bestScore)
            {
                bestScore = score;
                bestSrc = src;
            }
        }

        if (bestScore >= 0 && !string.IsNullOrEmpty(bestSrc))
            return Norm(bestSrc);

        var m = Regex.Match(html, @"//static\d*\.tgstat\.ru/channels/_0/[^""'\s>]+", RegexOptions.IgnoreCase);
        if (m.Success)
            return Norm(m.Value);

        m = AvatarChannels.Match(html);
        if (m.Success)
            return Norm(m.Groups[1].Value);

        m = Regex.Match(html, @"https://static\d*\.tgstat\.ru/channels/[^""'\s>]+", RegexOptions.IgnoreCase);
        return m.Success ? Norm(m.Value) : null;
    }

    /// <summary>Tgstat часто отвечает 403 без «живых» заголовков браузера.</summary>
    private static async Task<(string? Html, int StatusCode)> GetTgStatPageAsync(string url, string referer, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            req.Headers.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
            req.Headers.TryAddWithoutValidation("Referer", string.IsNullOrEmpty(referer) ? "https://tgstat.ru/" : referer);
            req.Headers.TryAddWithoutValidation("Origin", "https://tgstat.ru");
            req.Headers.TryAddWithoutValidation("Cache-Control", "max-age=0");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            req.Headers.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            req.Headers.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            req.Headers.TryAddWithoutValidation("Sec-Ch-Ua",
                "\"Chromium\";v=\"122\", \"Not(A:Brand\";v=\"24\", \"Google Chrome\";v=\"122\"");
            req.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Mobile", "?0");
            req.Headers.TryAddWithoutValidation("Sec-Ch-Ua-Platform", "\"Windows\"");

            using var resp = await TgStatSessionHttp.SendAsync(req, ct).ConfigureAwait(false);
            var code = (int)resp.StatusCode;
            if (!resp.IsSuccessStatusCode)
                return (null, code);
            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return (html, code);
        }
        catch
        {
            return (null, 0);
        }
    }

    internal static async Task<TgStatChannelFetch> FetchRecentPostsAsync(
        string? webOpenUrl,
        string telegramUsernameNoAt,
        DateTime utcNotBefore,
        CancellationToken ct)
    {
        var user = telegramUsernameNoAt.Trim().TrimStart('@');
        var baseUrl = BuildChannelBaseUrl(webOpenUrl, user);
        var all = new List<TelegramPublicPost>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? avatarHint = null;
        var lastStatus = 0;

        for (var page = 1; page <= 25; page++)
        {
            ct.ThrowIfCancellationRequested();
            var url = page == 1 ? baseUrl : baseUrl + "?page=" + page;
            var referer = page == 1 ? "https://tgstat.ru/" : baseUrl;
            var (html, status) = await GetTgStatPageAsync(url, referer, ct).ConfigureAwait(false);
            lastStatus = status;
            if (string.IsNullOrWhiteSpace(html))
                break;

            if (!LooksLikeTgStatChannel(html))
                break;

            if (page == 1)
                avatarHint = TryFindChannelAvatarUrl(html);

            var batch = ParsePostsPage(html, user, seen);
            if (batch.Count == 0)
            {
                if (page > 1)
                    break;
                // первая страница без блоков — нет ленты или другая вёрстка
                break;
            }

            var oldest = batch.Min(p => p.UtcDate);
            all.AddRange(batch);
            if (oldest < utcNotBefore)
                break;
        }

        var filtered = all.Where(p => p.UtcDate >= utcNotBefore).OrderByDescending(p => p.UtcDate).ToList();

        if (string.IsNullOrEmpty(avatarHint))
        {
            var (htmlAv, _) = await GetTgStatPageAsync(baseUrl, "https://tgstat.ru/", ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(htmlAv))
                avatarHint = TryFindChannelAvatarUrl(htmlAv);
        }

        return new TgStatChannelFetch(filtered, avatarHint, lastStatus);
    }

    private static bool LooksLikeTgStatChannel(string html) =>
        html.Contains("tgstat", StringComparison.OrdinalIgnoreCase) &&
        (html.Contains("post-container", StringComparison.OrdinalIgnoreCase) ||
         html.Contains("tgstat.ru/channel", StringComparison.OrdinalIgnoreCase));

    private static List<TelegramPublicPost> ParsePostsPage(string html, string usernameNoAt, HashSet<string> seen)
    {
        var list = new List<TelegramPublicPost>();
        var starts = new List<int>();
        foreach (Match m in PostIdDiv.Matches(html))
        {
            var gt = html.IndexOf('>', m.Index);
            if (gt < 0) continue;
            var openLen = gt - m.Index + 1;
            if (openLen > 512) continue;
            ReadOnlySpan<char> open = html.AsSpan(m.Index, openLen);
            if (open.Contains("post-container", StringComparison.OrdinalIgnoreCase))
            {
                starts.Add(m.Index);
                continue;
            }

            // Вариант вёрстки: post-container на родителе или только post-text + ссылка на пост
            var tailLen = Math.Min(4000, html.Length - m.Index);
            if (tailLen <= 0) continue;
            ReadOnlySpan<char> tail = html.AsSpan(m.Index, tailLen);
            if (tail.Contains("post-text", StringComparison.OrdinalIgnoreCase) &&
                tail.Contains("/channel/@", StringComparison.OrdinalIgnoreCase))
                starts.Add(m.Index);
        }

        if (starts.Count == 0)
            return list;

        for (var i = 0; i < starts.Count; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Count ? starts[i + 1] : html.Length;
            var len = Math.Max(0, end - start);
            if (len == 0) continue;
            var segment = html.AsSpan(start, len).ToString();

            var linkM = HrefChannelPost.Match(segment);
            if (!linkM.Success)
                continue;

            var path = linkM.Groups[1].Value.Trim();
            if (!path.Contains("@" + usernameNoAt, StringComparison.OrdinalIgnoreCase))
                continue;

            var postUrl = "https://tgstat.ru" + path;

            var idM = Regex.Match(path, @"/(\d+)$", RegexOptions.CultureInvariant);
            var postId = idM.Success ? idM.Groups[1].Value : path;

            if (!seen.Add(postUrl))
                continue;

            var smallM = SmallDate.Match(segment);
            var utc = smallM.Success
                ? ParseTgStatListingDate(smallM.Groups[1].Value)
                : DateTime.UtcNow;

            var plain = ExtractPostPlainText(segment);
            plain = WebUtility.HtmlDecode(plain);
            plain = Regex.Replace(plain, @"\s+", " ").Trim();

            list.Add(new TelegramPublicPost(postUrl, plain, utc, usernameNoAt, postId));
        }

        return list;
    }

    private static string ExtractPostPlainText(string segment)
    {
        var sb = new System.Text.StringBuilder();
        var pos = 0;
        while (pos < segment.Length)
        {
            var m = PostTextOpen.Match(segment, pos);
            if (!m.Success)
                break;
            var openEnd = m.Index + m.Length;
            var close = FindMatchingPostTextEnd(segment, openEnd);
            if (close < 0)
                break;
            var inner = segment[openEnd..close];
            var t = StripTags(inner);
            t = WebUtility.HtmlDecode(t);
            t = Regex.Replace(t, @"\s+", " ").Trim();
            if (t.Length > 0)
            {
                if (sb.Length > 0)
                    sb.Append(' ');
                sb.Append(t);
            }

            pos = close + 6;
        }

        return sb.Length > 0 ? sb.ToString() : StripTags(segment);
    }

    /// <summary>Закрывающий тег первого уровня для post-text с учётом вложенных div.</summary>
    private static int FindMatchingPostTextEnd(string s, int from)
    {
        var depth = 1;
        var i = from;
        while (i < s.Length && depth > 0)
        {
            var open = s.IndexOf("<div", i, StringComparison.OrdinalIgnoreCase);
            var close = s.IndexOf("</div>", i, StringComparison.OrdinalIgnoreCase);
            if (close < 0)
                return -1;
            if (open >= 0 && open < close)
            {
                depth++;
                i = open + 4;
            }
            else
            {
                depth--;
                if (depth == 0)
                    return close;
                i = close + 6;
            }
        }

        return -1;
    }

    private static string StripTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = Regex.Replace(html, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", " ");
        return s;
    }

    /// <summary>Строка вида «28 Mar, 11:19» — локальное время сайта, год подставляем.</summary>
    private static DateTime ParseTgStatListingDate(string raw)
    {
        var t = WebUtility.HtmlDecode(raw).Trim();
        string[] formats =
        [
            "d MMM, HH:mm", "dd MMM, HH:mm",
            "d MMMM, HH:mm", "dd MMMM, HH:mm",
            "d MMM yyyy, HH:mm", "dd MMM yyyy, HH:mm",
            "d MMMM yyyy, HH:mm", "dd MMMM yyyy, HH:mm",
            "dd.MM.yyyy HH:mm", "d.MM.yyyy HH:mm", "dd.MM.yy HH:mm", "d.MM.yy HH:mm"
        ];

        DateTime local;
        var ru = CultureInfo.GetCultureInfo("ru-RU");
        if (!DateTime.TryParseExact(t, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out local) &&
            !DateTime.TryParseExact(t, formats, ru, DateTimeStyles.None, out local) &&
            !DateTime.TryParse(t, ru, DateTimeStyles.None, out local) &&
            !DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out local))
        {
            return DateTime.UtcNow;
        }

        try
        {
            if (local.Year >= 2000)
                return DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();

            var y = DateTime.Now.Year;
            var withY = new DateTime(y, local.Month, local.Day, local.Hour, local.Minute, 0, DateTimeKind.Local);
            if (withY > DateTime.Now.AddDays(2))
                withY = withY.AddYears(-1);
            if (withY > DateTime.Now.AddDays(2))
                withY = withY.AddYears(-1);
            return withY.ToUniversalTime();
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}

internal sealed record TgStatChannelFetch(
    IReadOnlyList<TelegramPublicPost> Posts,
    string? AvatarUrlHint,
    int LastHttpStatus);
