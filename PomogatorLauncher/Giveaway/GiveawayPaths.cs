using System.IO;

namespace PomogatorLauncher.Giveaway;

internal static class GiveawayPaths
{
    internal static string RuleRoot =>
        Path.Combine(AppContext.BaseDirectory, "giveawayrule");
}
