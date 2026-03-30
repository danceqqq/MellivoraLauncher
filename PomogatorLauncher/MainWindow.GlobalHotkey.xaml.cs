using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace PomogatorLauncher;

public partial class MainWindow
{
    private IntPtr _nativeHwnd;
    private HwndSource? _hwndSource;
    private bool _globalHotkeyRegistered;
    /// <summary>Окно показано глобальной горячей клавишей; следующее нажатие сворачивает.</summary>
    private bool _hotkeySurfaced;
    private bool _hotkeyCaptureMode;

    private const int GlobalHotkeyId = 0x4D50;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _nativeHwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(_nativeHwnd);
        _hwndSource?.AddHook(NativeWndHook);
        ApplyRegisteredGlobalHotkey();
    }

    private IntPtr NativeWndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == GlobalHotkeyInterop.WM_HOTKEY && wParam.ToInt32() == GlobalHotkeyId)
        {
            Dispatcher.BeginInvoke(new Action(ToggleWindowFromGlobalHotkey));
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void ToggleWindowFromGlobalHotkey()
    {
        if (_hotkeySurfaced)
        {
            Topmost = false;
            WindowState = WindowState.Minimized;
            _hotkeySurfaced = false;
        }
        else
        {
            Topmost = true;
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Show();
            Activate();
            _hotkeySurfaced = true;
        }
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            _hotkeySurfaced = false;
    }

    private void ApplyRegisteredGlobalHotkey()
    {
        UnregisterGlobalHotkey();
        var display = AppSettingsStore.LoadGlobalHotkey();
        if (!GlobalHotkeyInterop.TryParse(display, out var mod, out var vk))
        {
            display = AppSettingsStore.DefaultGlobalHotkey;
            AppSettingsStore.SaveGlobalHotkey(display);
            if (!GlobalHotkeyInterop.TryParse(display, out mod, out vk))
                return;
        }

        if (_nativeHwnd == IntPtr.Zero)
            return;

        if (GlobalHotkeyInterop.TryRegister(_nativeHwnd, GlobalHotkeyId, mod, vk))
            _globalHotkeyRegistered = true;
    }

    private void UnregisterGlobalHotkey()
    {
        if (!_globalHotkeyRegistered || _nativeHwnd == IntPtr.Zero)
            return;
        GlobalHotkeyInterop.UnregisterHotKey(_nativeHwnd, GlobalHotkeyId);
        _globalHotkeyRegistered = false;
    }

    private void OpenSettingsPanel()
    {
        SettingsOverlay.Visibility = Visibility.Visible;
        GlobalHotkeyTextBox.Text = AppSettingsStore.LoadGlobalHotkey();
        _hotkeyCaptureMode = false;
    }

    private void OpenMellivoraVpnPanel()
    {
        MellivoraVpnOverlay.Visibility = Visibility.Visible;
        SyncTgBypassSettingsUi();
        RefreshZapretServiceButtonUi();
        RefreshMellivoraWarpCfgUi();
    }

    private void OpenScriptsPanel()
    {
        ScriptsOverlay.Visibility = Visibility.Visible;
    }

    private void ScriptsHomeButton_Click(object sender, RoutedEventArgs e)
    {
        ScriptsOverlay.Visibility = Visibility.Collapsed;
    }

    private void MellivoraVpnHomeButton_Click(object sender, RoutedEventArgs e)
    {
        MellivoraVpnOverlay.Visibility = Visibility.Collapsed;
    }

    private void SettingsHomeButton_Click(object sender, RoutedEventArgs e)
    {
        _hotkeyCaptureMode = false;
        GlobalHotkeyTextBox.Text = AppSettingsStore.LoadGlobalHotkey();
        SettingsOverlay.Visibility = Visibility.Collapsed;
    }

    private void GlobalHotkeyTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _hotkeyCaptureMode = true;
        GlobalHotkeyTextBox.Text = "Нажмите сочетание…";
        GlobalHotkeyTextBox.Focus();
    }

    private void GlobalHotkeyTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!_hotkeyCaptureMode) return;
        _hotkeyCaptureMode = false;
        GlobalHotkeyTextBox.Text = AppSettingsStore.LoadGlobalHotkey();
    }

    private void GlobalHotkeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_hotkeyCaptureMode) return;
        e.Handled = true;

        if (e.Key == Key.Escape)
        {
            _hotkeyCaptureMode = false;
            GlobalHotkeyTextBox.Text = AppSettingsStore.LoadGlobalHotkey();
            return;
        }

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            return;

        uint mod = 0;
        var km = Keyboard.Modifiers;
        if (km.HasFlag(ModifierKeys.Control)) mod |= GlobalHotkeyInterop.MOD_CONTROL;
        if (km.HasFlag(ModifierKeys.Alt)) mod |= GlobalHotkeyInterop.MOD_ALT;
        if (km.HasFlag(ModifierKeys.Shift)) mod |= GlobalHotkeyInterop.MOD_SHIFT;
        if (km.HasFlag(ModifierKeys.Windows)) mod |= GlobalHotkeyInterop.MOD_WIN;

        if (mod == 0)
            return;

        var vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
            return;

        var display = GlobalHotkeyInterop.Format(mod, vk);
        if (!GlobalHotkeyInterop.TryParse(display, out _, out _))
            return;

        UnregisterGlobalHotkey();
        if (!GlobalHotkeyInterop.TryRegister(_nativeHwnd, GlobalHotkeyId, mod, vk))
        {
            MessageBox.Show(this,
                "Не удалось назначить сочетание (часто оно уже занято другой программой).",
                "Помогатор",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            ApplyRegisteredGlobalHotkey();
            GlobalHotkeyTextBox.Text = AppSettingsStore.LoadGlobalHotkey();
            _hotkeyCaptureMode = false;
            return;
        }

        _globalHotkeyRegistered = true;
        AppSettingsStore.SaveGlobalHotkey(display);
        GlobalHotkeyTextBox.Text = display;
        _hotkeyCaptureMode = false;
    }
}
