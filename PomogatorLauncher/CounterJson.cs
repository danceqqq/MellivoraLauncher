using System.IO;
using System.Text.Json;

namespace PomogatorLauncher;

internal static class CounterJson
{
    /// <summary>Читает отображаемое значение: приоритет <c>value</c> (строка), затем <c>online</c>, затем <c>count</c>.</summary>
    public static string ReadDisplay(string path)
    {
        try
        {
            if (!File.Exists(path)) return "—";
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;

            if (root.TryGetProperty("value", out var valueEl))
            {
                if (valueEl.ValueKind == JsonValueKind.String)
                {
                    var s = valueEl.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!;
                }

                if (valueEl.ValueKind == JsonValueKind.Number)
                    return FormatNumber(valueEl);
            }

            if (root.TryGetProperty("online", out var onlineEl))
            {
                if (onlineEl.ValueKind == JsonValueKind.String)
                {
                    var s = onlineEl.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s!;
                }

                if (onlineEl.ValueKind == JsonValueKind.Number)
                    return FormatNumber(onlineEl);
            }

            if (root.TryGetProperty("count", out var countEl) && countEl.ValueKind == JsonValueKind.Number)
                return FormatNumber(countEl);
        }
        catch
        {
            /* файл битый или пустой */
        }

        return "—";
    }

    private static string FormatNumber(JsonElement el)
    {
        if (el.TryGetInt32(out var i)) return i.ToString();
        if (el.TryGetInt64(out var l)) return l.ToString();
        return el.GetRawText();
    }
}
