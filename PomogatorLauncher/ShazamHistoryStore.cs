using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PomogatorLauncher;

internal static class ShazamHistoryStore
{
    private const int MaxEntries = 10;
    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static string HistoryPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PomogatorLauncher",
            "shazam_history.json");

    internal static void LoadInto(ObservableCollection<ShazamTrackViewModel> target)
    {
        target.Clear();
        try
        {
            var path = HistoryPath;
            if (!File.Exists(path)) return;
            var list = JsonSerializer.Deserialize<List<ShazamHistoryDto>>(File.ReadAllText(path, Utf8NoBom));
            if (list == null) return;
            foreach (var dto in list.OrderByDescending(x => x.AtUtc).Take(MaxEntries))
                target.Add(new ShazamTrackViewModel(dto.Title, dto.Artist, dto.CoverUrl, dto.AtUtc));
        }
        catch
        {
            /* ignore */
        }
    }

    internal static void PrependAndSave(ObservableCollection<ShazamTrackViewModel> all, ShazamTrackViewModel entry)
    {
        for (var i = all.Count - 1; i >= 0; i--)
        {
            var x = all[i];
            if (string.Equals(x.Title, entry.Title, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Artist, entry.Artist, StringComparison.OrdinalIgnoreCase))
                all.RemoveAt(i);
        }

        all.Insert(0, entry);
        while (all.Count > MaxEntries)
            all.RemoveAt(all.Count - 1);

        try
        {
            var dir = Path.GetDirectoryName(HistoryPath)!;
            Directory.CreateDirectory(dir);
            var dtos = all.Select(x => new ShazamHistoryDto
            {
                Title = x.Title,
                Artist = x.Artist,
                CoverUrl = x.CoverUrl,
                AtUtc = x.RecognizedAtUtc
            }).ToList();
            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(dtos, JsonOpt), Utf8NoBom);
        }
        catch
        {
            /* ignore */
        }
    }

    private sealed class ShazamHistoryDto
    {
        public string Title { get; set; } = "";
        public string Artist { get; set; } = "";
        public string? CoverUrl { get; set; }
        public DateTime AtUtc { get; set; }
    }
}
