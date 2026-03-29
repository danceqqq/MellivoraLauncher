using System.IO;

namespace PomogatorLauncher;

/// <summary>
/// Поиск установленного <see href="https://github.com/amnezia-vpn/amnezia-client">Amnezia VPN</see> (полноценный клиент не вшиваем — только запуск).
/// </summary>
internal static class AmneziaVpnLocator
{
    internal const string ClientReleasesUrl = "https://github.com/amnezia-vpn/amnezia-client/releases";

    internal static string? FindExecutable()
    {
        foreach (var p in GetCandidatePaths())
        {
            if (File.Exists(p))
                return p;
        }

        return null;
    }

    private static IEnumerable<string> GetCandidatePaths()
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        yield return Path.Combine(pf, "AmneziaVPN", "AmneziaVPN.exe");
        yield return Path.Combine(pfx86, "AmneziaVPN", "AmneziaVPN.exe");
        yield return Path.Combine(la, "Programs", "AmneziaVPN", "AmneziaVPN.exe");
        yield return Path.Combine(la, "AmneziaVPN", "AmneziaVPN.exe");
    }
}
