using System.Net;
using System.Net.Http;
using System.Net.Security;
using Microsoft.Web.WebView2.Core;
using PomogatorLauncher;

namespace PomogatorLauncher.Giveaway;

/// <summary>
/// HttpClient для tgstat.ru с <see cref="CookieContainer"/> (куки из WebView2 после входа).
/// Прокси совпадает с <see cref="TelegramPublicChannelParser"/>.
/// </summary>
internal static class TgStatSessionHttp
{
    private static readonly object Lock = new();
    private static HttpClient? _client;
    private static CookieContainer _jar = new();
    internal static int LastImportedCookieCount { get; private set; }

    internal static void InvalidateClientAfterProxyChange()
    {
        lock (Lock)
        {
            _client?.Dispose();
            _client = null;
        }
    }

    /// <summary>Заменить jar куками из профиля WebView2 (после «Применить куки»).</summary>
    internal static void ReplaceCookiesFromWebView2(IReadOnlyList<CoreWebView2Cookie> webCookies)
    {
        var jar = new CookieContainer();
        foreach (var wc in webCookies)
        {
            try
            {
                var path = string.IsNullOrEmpty(wc.Path) ? "/" : wc.Path;
                var domain = wc.Domain;
                if (string.IsNullOrEmpty(domain))
                    domain = ".tgstat.ru";
                if (!domain.StartsWith('.'))
                    domain = "." + domain.TrimStart('.');
                var host = domain.TrimStart('.');
                var uri = new Uri("https://" + host + "/");
                var nc = new Cookie(wc.Name, wc.Value, path, domain)
                {
                    Secure = wc.IsSecure,
                    HttpOnly = wc.IsHttpOnly
                };
                if (wc.Expires != DateTime.MinValue && wc.Expires.Year > 1970)
                    nc.Expires = wc.Expires;
                jar.Add(uri, nc);
            }
            catch
            {
                /* ignore malformed */
            }
        }

        lock (Lock)
        {
            LastImportedCookieCount = webCookies.Count;
            _jar = jar;
            _client?.Dispose();
            _client = null;
        }
    }

    internal static HttpClient GetClient()
    {
        lock (Lock)
        {
            if (_client != null)
                return _client;
            var useProxy = TelegramPublicChannelParser.IsSocks5ProxyEnabled;
            var handler = new SocketsHttpHandler
            {
                CookieContainer = _jar,
                UseCookies = true,
                AutomaticDecompression = DecompressionMethods.All,
                PooledConnectionLifetime = TimeSpan.FromMinutes(3),
                ConnectTimeout = TimeSpan.FromSeconds(25),
                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                          System.Security.Authentication.SslProtocols.Tls13
                },
                UseProxy = useProxy,
                Proxy = useProxy ? new WebProxy(TgWsProxyHost.Socks5Loopback) : null
            };

            _client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(45) };
            _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            return _client;
        }
    }

    internal static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        return await GetClient().SendAsync(request, ct).ConfigureAwait(false);
    }
}
