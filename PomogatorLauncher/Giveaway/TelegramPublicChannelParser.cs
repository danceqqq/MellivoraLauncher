using System.Globalization;
using System.Net;
using PomogatorLauncher;
using System.Net.Http;
using System.Net.Security;
using System.Text.RegularExpressions;

namespace PomogatorLauncher.Giveaway;

/// <summary>
/// Парсинг публичной ленты t.me/s/username (без Bot API). Несколько хостов — часть блокировок обходит только зеркало.
/// При включённом обходе трафик идёт через SOCKS5 (TG WS Proxy, .NET 8: <c>socks5://</c> в <see cref="SocketsHttpHandler"/>).
/// </summary>
internal static class TelegramPublicChannelParser
{
    /// <summary>Порядок важен: пробуем по очереди, пока не получим разбор сообщений.</summary>
    private static readonly string[] PreviewPageBases =
    [
        "https://t.me/s/",
        "https://telegram.me/s/"
    ];

    private static readonly object HttpLock = new();
    private static HttpClient? _http;
    private static bool _useSocks5;

    internal static bool IsSocks5ProxyEnabled
    {
        get
        {
            lock (HttpLock)
            {
                return _useSocks5;
            }
        }
    }

    /// <summary>Включает или выключает маршрутизацию запросов через <see cref="TgWsProxyHost.Socks5Loopback"/>.</summary>
    internal static void SetUseSocks5Proxy(bool use)
    {
        lock (HttpLock)
        {
            if (_useSocks5 == use && _http != null)
                return;
            _useSocks5 = use;
            _http?.Dispose();
            _http = CreateHttpClient();
        }

        TgStatSessionHttp.InvalidateClientAfterProxyChange();
    }

    /// <summary>Общий клиент для t.me и tgstat (прокси TG WS Proxy).</summary>
    internal static HttpClient Http
    {
        get
        {
            lock (HttpLock)
            {
                return _http ??= CreateHttpClient();
            }
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(3),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                      System.Security.Authentication.SslProtocols.Tls13
            },
            UseProxy = _useSocks5,
            Proxy = _useSocks5 ? new WebProxy(TgWsProxyHost.Socks5Loopback) : null
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(35)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept",
            "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "ru-RU,ru;q=0.9,en-US;q=0.8,en;q=0.7");
        return client;
    }

    internal static async Task<IReadOnlyList<TelegramPublicPost>> FetchRecentPostsAsync(
        string username,
        DateTime utcNotBefore,
        CancellationToken ct)
    {
        username = username.TrimStart('@');
        var enc = Uri.EscapeDataString(username);
        var all = new List<TelegramPublicPost>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var page = 1; page <= 20; page++)
        {
            ct.ThrowIfCancellationRequested();

            List<TelegramPublicPost>? batch = null;
            foreach (var pageBase in PreviewPageBases)
            {
                var url = page == 1
                    ? $"{pageBase}{enc}"
                    : $"{pageBase}{enc}?page={page}";

                string html;
                try
                {
                    html = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
                }
                catch
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(html) || !LooksLikeTelegramPreviewMarkup(html))
                    continue;

                var tryBatch = ParsePage(html, username, seen);
                if (tryBatch.Count == 0)
                    continue;

                batch = tryBatch;
                break;
            }

            if (batch == null || batch.Count == 0)
                break;

            var oldestInBatch = batch.Min(p => p.UtcDate);
            all.AddRange(batch);

            if (oldestInBatch < utcNotBefore)
                break;
        }

        return all.Where(p => p.UtcDate >= utcNotBefore).OrderByDescending(p => p.UtcDate).ToList();
    }

    private static bool LooksLikeTelegramPreviewMarkup(string html) =>
        html.Contains("tgme_widget_message_wrap", StringComparison.Ordinal) ||
        html.Contains("data-post=\"", StringComparison.Ordinal);

    private static List<TelegramPublicPost> ParsePage(string html, string username, HashSet<string> seen)
    {
        var list = new List<TelegramPublicPost>();
        var parts = html.Split(new[] { "tgme_widget_message_wrap" }, StringSplitOptions.None);
        foreach (var segment in parts)
        {
            var dm = Regex.Match(segment, @"data-post=""([^""/]+)/(\d+)""", RegexOptions.CultureInvariant);
            if (!dm.Success)
                continue;

            var ch = dm.Groups[1].Value;
            var id = dm.Groups[2].Value;
            var key = ch + "/" + id;
            if (!seen.Add(key))
                continue;

            DateTime utc;
            var timeM = Regex.Match(segment, @"datetime=""([^""]+)""", RegexOptions.CultureInvariant);
            if (timeM.Success)
            {
                if (!DateTime.TryParse(timeM.Groups[1].Value, CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out utc))
                    utc = DateTime.UtcNow;
            }
            else
                utc = DateTime.UtcNow;

            // Вложенные <div> в теле поста: узкий regex обрывался на первом </div> и терял текст (в т.ч. «розыгрыш»).
            var plain = StripTags(segment);
            plain = WebUtility.HtmlDecode(plain);
            plain = Regex.Replace(plain, @"\s+", " ").Trim();
            plain = Regex.Replace(plain, @"Please open Telegram to view this post", " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            plain = Regex.Replace(plain, @"VIEW IN TELEGRAM", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            plain = Regex.Replace(plain, @"\s+", " ").Trim();

            var postUrl = $"https://t.me/{ch}/{id}";
            list.Add(new TelegramPublicPost(postUrl, plain, utc, ch, id));
        }

        return list;
    }

    private static string StripTags(string html)
    {
        if (string.IsNullOrEmpty(html)) return "";
        var s = Regex.Replace(html, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, "<[^>]+>", " ");
        return s;
    }
}

internal sealed record TelegramPublicPost(string PostUrl, string PlainText, DateTime UtcDate, string Channel, string MessageId);
