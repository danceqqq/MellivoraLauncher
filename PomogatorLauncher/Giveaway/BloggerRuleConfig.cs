using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PomogatorLauncher.Giveaway;

internal sealed class BloggerRuleConfig
{
    private static readonly JsonSerializerOptions WebJsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    internal required string FolderSlug { get; init; }
    internal required string DisplayName { get; init; }
    internal required string TelegramUsername { get; init; }
    internal string? WebOpenUrl { get; init; }
    internal string? AvatarFilePath { get; init; }

    internal static BloggerRuleConfig? TryLoad(string bloggerFolder)
    {
        var slug = Path.GetFileName(bloggerFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var jsonPath = Path.Combine(bloggerFolder, "web.json");
        if (!File.Exists(jsonPath))
            return null;

        var raw = File.ReadAllText(jsonPath).Trim();
        string? displayName = null;
        string? username = null;
        string? webOpen = null;

        if (raw.StartsWith('{'))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<WebJsonDto>(raw, WebJsonReadOptions);
                displayName = dto?.DisplayName?.Trim();
                username = dto?.Telegram?.Username?.Trim().TrimStart('@');
                webOpen = dto?.Telegram?.WebOpenUrl?.Trim();
            }
            catch
            {
                return null;
            }
        }
        else
        {
            webOpen = raw;
            var m = Regex.Match(raw, @"#@([a-zA-Z0-9_]+)", RegexOptions.CultureInvariant);
            if (m.Success)
                username = m.Groups[1].Value;
        }

        if (string.IsNullOrEmpty(username))
            return null;

        displayName ??= CapitalizeSlug(slug);
        var avatarDir = Path.Combine(bloggerFolder, "avatar");
        string? avatarFile = null;
        if (Directory.Exists(avatarDir))
        {
            foreach (var ext in new[] { "png", "jpg", "jpeg", "webp", "bmp" })
            {
                var found = Directory.GetFiles(avatarDir, "*." + ext, SearchOption.TopDirectoryOnly)
                    .Concat(Directory.GetFiles(avatarDir, "*." + ext.ToUpperInvariant(), SearchOption.TopDirectoryOnly))
                    .FirstOrDefault();
                if (found != null)
                {
                    avatarFile = found;
                    break;
                }
            }
        }

        return new BloggerRuleConfig
        {
            FolderSlug = slug,
            DisplayName = displayName,
            TelegramUsername = username,
            WebOpenUrl = webOpen,
            AvatarFilePath = avatarFile
        };
    }

    private static string CapitalizeSlug(string slug)
    {
        if (string.IsNullOrEmpty(slug)) return slug;
        return char.ToUpperInvariant(slug[0]) + slug[1..];
    }

    private sealed class WebJsonDto
    {
        public string? DisplayName { get; set; }
        public TelegramDto? Telegram { get; set; }
    }

    private sealed class TelegramDto
    {
        public string? Username { get; set; }
        public string? WebOpenUrl { get; set; }
    }
}
