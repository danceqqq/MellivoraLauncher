using System.Windows.Media;

namespace PomogatorLauncher.Giveaway;

internal sealed class GiveawayStripItemVm
{
    internal GiveawayStripItemVm(string displayName, bool hasGiveawayHit, ImageSource colorAvatar, ImageSource grayAvatar)
    {
        DisplayName = displayName;
        HasGiveawayHit = hasGiveawayHit;
        ColorAvatar = colorAvatar;
        GrayAvatar = grayAvatar;
    }

    internal string DisplayName { get; }
    internal bool HasGiveawayHit { get; }
    internal ImageSource ColorAvatar { get; }
    internal ImageSource GrayAvatar { get; }

    internal ImageSource StripImageSource => HasGiveawayHit ? ColorAvatar : GrayAvatar;
}

internal sealed class GiveawayPostTileVm
{
    internal GiveawayPostTileVm(
        string bloggerName,
        ImageSource avatar,
        string previewText,
        string postUrl,
        string postedAtDisplay,
        DateTime postedAtUtc)
    {
        BloggerName = bloggerName;
        Avatar = avatar;
        PreviewText = previewText;
        PostUrl = postUrl;
        PostedAtDisplay = postedAtDisplay;
        PostedAtUtc = postedAtUtc;
    }

    internal string BloggerName { get; }
    internal ImageSource Avatar { get; }
    internal string PreviewText { get; }
    internal string PostUrl { get; }
    internal string PostedAtDisplay { get; }
    internal DateTime PostedAtUtc { get; }
}
