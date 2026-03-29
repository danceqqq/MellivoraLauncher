using System.Runtime.InteropServices;
using System.Text;

namespace PomogatorLauncher;

internal static class GlobalHotkeyInterop
{
    internal const uint MOD_ALT = 0x0001;
    internal const uint MOD_CONTROL = 0x0002;
    internal const uint MOD_SHIFT = 0x0004;
    internal const uint MOD_WIN = 0x0008;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    internal static bool TryParse(string? s, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2) return false;

        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToUpperInvariant())
            {
                case "ALT":
                    modifiers |= MOD_ALT;
                    break;
                case "CTRL":
                case "CONTROL":
                    modifiers |= MOD_CONTROL;
                    break;
                case "SHIFT":
                    modifiers |= MOD_SHIFT;
                    break;
                case "WIN":
                case "WINDOWS":
                    modifiers |= MOD_WIN;
                    break;
                default:
                    return false;
            }
        }

        if (modifiers == 0) return false;
        return TryParseKeyToken(parts[^1], out vk);
    }

    private static bool TryParseKeyToken(string t, out uint vk)
    {
        vk = 0;
        t = t.Trim();
        if (t.Length == 0) return false;

        if (t.Length == 1)
        {
            var c = t[0];
            if (c is >= 'a' and <= 'z')
            {
                vk = (uint)(c - 'a' + 'A');
                return true;
            }

            if (c is >= 'A' and <= 'Z')
            {
                vk = (uint)c;
                return true;
            }

            if (c is >= '0' and <= '9')
            {
                vk = (uint)c;
                return true;
            }

            return false;
        }

        if (t.Length >= 2 && (t[0] == 'F' || t[0] == 'f') && int.TryParse(t.AsSpan(1), out var fn) && fn is >= 1 and <= 24)
        {
            vk = (uint)(0x6F + fn);
            return true;
        }

        return false;
    }

    internal static string Format(uint modifiers, uint vk)
    {
        var sb = new StringBuilder();
        if ((modifiers & MOD_ALT) != 0) Append(sb, "Alt");
        if ((modifiers & MOD_CONTROL) != 0) Append(sb, "Ctrl");
        if ((modifiers & MOD_SHIFT) != 0) Append(sb, "Shift");
        if ((modifiers & MOD_WIN) != 0) Append(sb, "Win");
        Append(sb, FormatVk(vk));
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string part)
    {
        if (sb.Length > 0) sb.Append('+');
        sb.Append(part);
    }

    private static string FormatVk(uint vk)
    {
        if (vk is >= 0x30 and <= 0x39)
            return ((char)vk).ToString();
        if (vk is >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87)
            return "F" + (vk - 0x6F);
        return "0x" + vk.ToString("X");
    }

    /// <summary>
    /// Регистрация с MOD_NOREPEAT; при неудаче — без NOREPEAT.
    /// </summary>
    internal static bool TryRegister(IntPtr hwnd, int id, uint modifiers, uint vk)
    {
        var m = modifiers | MOD_NOREPEAT;
        if (RegisterHotKey(hwnd, id, m, vk))
            return true;
        return RegisterHotKey(hwnd, id, modifiers, vk);
    }
}
