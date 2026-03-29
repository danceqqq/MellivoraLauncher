using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PomogatorLauncher.Giveaway;

internal sealed record GiveawayScanSummary(int PostTileCount, string? StatusOverrideMessage);

internal static class GiveawayTelegramScanService
{
    internal static bool TextLooksLikeGiveaway(string? plain)
    {
        if (string.IsNullOrWhiteSpace(plain)) return false;
        var t = plain.ToLowerInvariant().Normalize(NormalizationForm.FormC);
        return t.Contains("розыгрыш", StringComparison.Ordinal) || t.Contains("розырыш", StringComparison.Ordinal)
               || t.Contains("разыгрыш", StringComparison.Ordinal) || t.Contains("розигрыш", StringComparison.Ordinal)
               || t.Contains("giveaway", StringComparison.Ordinal) || t.Contains("конкурс", StringComparison.Ordinal);
    }

    internal static async Task<GiveawayScanSummary> ScanIntoAsync(
        ObservableCollection<GiveawayStripItemVm> strip,
        ObservableCollection<GiveawayPostTileVm> tiles,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var root = GiveawayPaths.RuleRoot;
        if (!Directory.Exists(root))
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                strip.Clear();
                tiles.Clear();
            });
            var msg = "Папка giveawayrule не найдена.";
            status?.Report(msg);
            return new GiveawayScanSummary(0, msg);
        }

        var bloggerDirs = Directory.GetDirectories(root);
        var configs = new List<BloggerRuleConfig>();
        foreach (var dir in bloggerDirs)
        {
            var cfg = BloggerRuleConfig.TryLoad(dir);
            if (cfg != null)
                configs.Add(cfg);
        }

        if (configs.Count == 0)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                strip.Clear();
                tiles.Clear();
            });
            var msg = "Нет блогеров в giveawayrule.";
            status?.Report(msg);
            return new GiveawayScanSummary(0, msg);
        }

        var utcMonthAgo = DateTime.UtcNow.AddDays(-30);
        var rows = new List<GiveawayScanRow>();

        foreach (var cfg in configs)
        {
            ct.ThrowIfCancellationRequested();
            status?.Report($"Проверка: {cfg.DisplayName} (tgstat.ru)…");

            TgStatChannelFetch fetch;
            try
            {
                fetch = await TgStatChannelParser
                    .FetchRecentPostsAsync(cfg.WebOpenUrl, cfg.TelegramUsername, utcMonthAgo, ct)
                    .ConfigureAwait(false);
            }
            catch
            {
                fetch = new TgStatChannelFetch(Array.Empty<TelegramPublicPost>(), null, 0);
            }

            if (fetch.Posts.Count == 0)
            {
                try
                {
                    var tme = await TelegramPublicChannelParser
                        .FetchRecentPostsAsync(cfg.TelegramUsername, utcMonthAgo, ct)
                        .ConfigureAwait(false);
                    if (tme.Count > 0)
                        fetch = new TgStatChannelFetch(tme, fetch.AvatarUrlHint, fetch.LastHttpStatus);
                }
                catch
                {
                    /* ignore */
                }
            }

            var posts = fetch.Posts;
            var hits = posts.Where(p => TextLooksLikeGiveaway(p.PlainText)).ToList();
            rows.Add(new GiveawayScanRow(cfg, hits.Count > 0, hits, fetch.AvatarUrlHint));
        }

        var enriched = new List<(GiveawayScanRow Row, BitmapSource? Avatar)>();
        foreach (var r in rows)
        {
            ct.ThrowIfCancellationRequested();
            var av = TryLoadAvatar(r.Config.AvatarFilePath);
            if (av == null && !string.IsNullOrEmpty(r.AvatarUrlHint))
                av = await TryDownloadAvatarAsync(r.AvatarUrlHint, r.Config, ct).ConfigureAwait(false);
            enriched.Add((r, av));
        }

        var orderedRows = enriched
            .OrderByDescending(x => x.Row.HasHit)
            .ThenBy(x => x.Row.Config.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            strip.Clear();
            tiles.Clear();

            var tileBuffer = new List<GiveawayPostTileVm>();

            foreach (var (r, downloadedOrFileAvatar) in orderedRows)
            {
                var cfg = r.Config;
                var colorAvatar = downloadedOrFileAvatar;
                var grayAvatar = colorAvatar != null ? ToGrayscale(colorAvatar) : null;
                var showColor = colorAvatar ?? CreatePlaceholder(cfg.DisplayName);
                var showGray = grayAvatar ?? (colorAvatar != null ? ToGrayscale(colorAvatar) : showColor);

                strip.Add(new GiveawayStripItemVm(cfg.DisplayName, r.HasHit, showColor, showGray));

                foreach (var hit in r.Hits.OrderByDescending(h => h.UtcDate))
                {
                    var preview = hit.PlainText.Length > 320
                        ? hit.PlainText[..320] + "…"
                        : hit.PlainText;
                    var whenLocal = hit.UtcDate.ToLocalTime()
                        .ToString("d", CultureInfo.GetCultureInfo("ru-RU"));
                    var av = colorAvatar ?? CreatePlaceholder(cfg.DisplayName);
                    tileBuffer.Add(new GiveawayPostTileVm(
                        cfg.DisplayName,
                        av,
                        preview,
                        hit.PostUrl,
                        whenLocal,
                        hit.UtcDate));
                }
            }

            foreach (var t in tileBuffer.OrderByDescending(x => x.PostedAtUtc))
                tiles.Add(t);
        });

        return new GiveawayScanSummary(
            orderedRows.Sum(x => x.Row.Hits.Count),
            null);
    }

    private sealed record GiveawayScanRow(
        BloggerRuleConfig Config,
        bool HasHit,
        List<TelegramPublicPost> Hits,
        string? AvatarUrlHint);

    private static async Task<BitmapSource?> TryDownloadAvatarAsync(string url, BloggerRuleConfig cfg, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            var referer = TgStatChannelParser.BuildChannelBaseUrl(cfg.WebOpenUrl, cfg.TelegramUsername);
            req.Headers.TryAddWithoutValidation("Referer", referer);
            req.Headers.TryAddWithoutValidation("Accept", "image/avif,image/webp,image/apng,image/*,*/*;q=0.8");
            using var resp = url.Contains("tgstat", StringComparison.OrdinalIgnoreCase)
                ? await TgStatSessionHttp.SendAsync(req, ct).ConfigureAwait(false)
                : await TelegramPublicChannelParser.Http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length == 0) return null;
            var ms = new MemoryStream(bytes, writable: false);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static BitmapSource? TryLoadAvatar(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = fs;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 128;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gray8 в WPF часто не рисуется в <see cref="System.Windows.Controls.Image"/> (чёрный круг).
    /// Делаем оттенки серого в Pbgra32.
    /// </summary>
    private static BitmapSource ToGrayscale(BitmapSource src)
    {
        try
        {
            var px = PixelFormats.Pbgra32;
            var conv = new FormatConvertedBitmap(src, px, null, 0);
            conv.Freeze();

            var w = conv.PixelWidth;
            var h = conv.PixelHeight;
            var stride = (w * px.BitsPerPixel + 7) / 8;
            var buf = new byte[stride * h];
            conv.CopyPixels(buf, stride, 0);

            for (var i = 0; i < buf.Length; i += 4)
            {
                var b = buf[i];
                var g = buf[i + 1];
                var r = buf[i + 2];
                var a = buf[i + 3];
                if (a == 0)
                    continue;
                var y = (byte)(0.299 * r + 0.587 * g + 0.114 * b);
                buf[i] = y;
                buf[i + 1] = y;
                buf[i + 2] = y;
            }

            var wb = new WriteableBitmap(w, h, 96, 96, px, null);
            wb.WritePixels(new Int32Rect(0, 0, w, h), buf, stride, 0);
            wb.Freeze();
            return wb;
        }
        catch
        {
            return src;
        }
    }

    private static ImageSource CreatePlaceholder(string name)
    {
        var letter = string.IsNullOrEmpty(name) ? "?" : char.ToUpperInvariant(name.Trim()[0]).ToString();
        var pixelsPerDip = Application.Current?.MainWindow is Window w
            ? VisualTreeHelper.GetDpi(w).PixelsPerDip
            : 1.0;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), null, new Rect(0, 0, 64, 64));
            var ft = new FormattedText(
                letter,
                CultureInfo.GetCultureInfo("ru-RU"),
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                28,
                Brushes.White,
                pixelsPerDip);
            dc.DrawText(ft, new Point(20, 14));
        }

        var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
