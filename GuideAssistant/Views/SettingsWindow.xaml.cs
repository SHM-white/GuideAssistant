using GuideAssistant.Data;
using GuideAssistant.Models;
using GuideAssistant.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace GuideAssistant.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly HotkeyRepository _hotkeyRepo;
    private readonly HotkeyService _hotkeyService;
    private readonly Action? _onHotkeysChanged;
    private int _capturingActionIndex;
    private bool _isSubtitleEnabled;
    private bool _isMiniMapEnabled;
    private readonly Action<bool>? _onSubtitleToggled;
    private readonly Action<bool>? _onMinimapToggled;
    private readonly Func<double>? _getOpacity;
    private readonly Action<double>? _setOpacity;

    private static readonly (string ActionName, string DisplayName)[] ActionDefs =
    {
        ("play_pause",       "播放 / 暂停"),
        ("fast_forward",     "快进"),
        ("fast_backward",    "快退"),
        ("volume_up",        "音量 +"),
        ("volume_down",      "音量 -"),
        ("toggle_visibility","隐藏 / 显示窗口"),
        ("bookmark_page",    "收藏页面"),
        ("toggle_subtitle",  "字幕切换"),
        ("toggle_minimap",   "小地图切换"),
    };

    public SettingsWindow(
        HotkeyRepository hotkeyRepo,
        HotkeyService hotkeyService,
        bool isSubtitleEnabled,
        bool isMiniMapEnabled,
        Action? onHotkeysChanged = null,
        Action<bool>? onSubtitleToggled = null,
        Action<bool>? onMinimapToggled = null,
        Func<double>? getOpacity = null,
        Action<double>? setOpacity = null)
    {
        InitializeComponent();

        _hotkeyRepo = hotkeyRepo;
        _hotkeyService = hotkeyService;
        _onHotkeysChanged = onHotkeysChanged;
        _isSubtitleEnabled = isSubtitleEnabled;
        _isMiniMapEnabled = isMiniMapEnabled;
        _onSubtitleToggled = onSubtitleToggled;
        _onMinimapToggled = onMinimapToggled;
        _getOpacity = getOpacity;
        _setOpacity = setOpacity;

        SetupWindow();
        LoadHotkeys();
        LoadDisplaySettings();
        SettingsNav.SelectedItem = SettingsNav.MenuItems[0];
    }

    private void SetupWindow()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 560, Height = 480 });
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
        {
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
        }
    }

    // ── Navigation ────────────────────────────────────────────────

    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            HotkeysPage.Visibility = Visibility.Collapsed;
            DisplayPage.Visibility = Visibility.Collapsed;
            AboutPage.Visibility = Visibility.Collapsed;

            switch (item.Tag?.ToString())
            {
                case "hotkeys": HotkeysPage.Visibility = Visibility.Visible; break;
                case "display": DisplayPage.Visibility = Visibility.Visible; break;
                case "about":   AboutPage.Visibility   = Visibility.Visible; break;
            }
        }
    }

    // ── Hotkeys ────────────────────────────────────────────────────

    private List<HotkeyRow> BuildHotkeyRows()
    {
        var profile = _hotkeyRepo.GetDefaultProfile();
        var rows = new List<HotkeyRow>();
        foreach (var def in ActionDefs)
        {
            var binding = profile?.Bindings.FirstOrDefault(b => b.ActionName == def.ActionName);
            rows.Add(new HotkeyRow
            {
                ActionName = def.ActionName,
                DisplayName = def.DisplayName,
                CurrentBinding = (binding != null && binding.VirtualKey != 0)
                    ? binding.DisplayText
                    : "未绑定"
            });
        }
        return rows;
    }

    private void LoadHotkeys()
    {
        HotkeyList.ItemsSource = BuildHotkeyRows();
    }

    private void HotkeyRebind_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is HotkeyRow row)
        {
            int idx = Array.FindIndex(ActionDefs, a => a.ActionName == row.ActionName);
            if (idx < 0) return;

            _capturingActionIndex = idx;
            btn.Content = "按下按键…";
            btn.IsEnabled = false;
            CaptureHint.Opacity = 1;

            if (Content is FrameworkElement fe)
                fe.KeyDown += SettingsWindow_KeyDown;
        }
    }

    private void FinishCapture()
    {
        if (Content is FrameworkElement fe)
            fe.KeyDown -= SettingsWindow_KeyDown;

        CaptureHint.Opacity = 0;
        HotkeyList.ItemsSource = BuildHotkeyRows();
    }

    private void HotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is HotkeyRow row)
        {
            var profile = _hotkeyRepo.GetDefaultProfile();
            if (profile == null) return;

            var binding = profile.Bindings.FirstOrDefault(b => b.ActionName == row.ActionName);
            if (binding != null)
            {
                binding.Modifiers = 0;
                binding.VirtualKey = 0;
                binding.DisplayText = "";
                _hotkeyRepo.SaveBindings(profile.Id, profile.Bindings);
                HotkeyList.ItemsSource = BuildHotkeyRows();
                _onHotkeysChanged?.Invoke();
                Log.Information("Hotkey cleared: {Action}", row.ActionName);
            }
        }
    }

    private void SettingsWindow_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            FinishCapture();
            return;
        }

        int vkCode = (int)e.Key;
        SaveHotkeyBinding(_capturingActionIndex, (uint)vkCode);
        _onHotkeysChanged?.Invoke();
        FinishCapture();
    }

    private void SaveHotkeyBinding(int actionIndex, uint virtualKey)
    {
        var profile = _hotkeyRepo.GetDefaultProfile();
        if (profile == null) return;

        var actionName = ActionDefs[actionIndex].ActionName;
        var displayName = ActionDefs[actionIndex].DisplayName;

        var binding = profile.Bindings.FirstOrDefault(b => b.ActionName == actionName);
        if (binding == null)
        {
            binding = new HotkeyBinding
            {
                ActionName = actionName,
                ActionDisplay = displayName,
                ProfileId = profile.Id
            };
            profile.Bindings.Add(binding);
        }

        binding.VirtualKey = virtualKey;
        binding.DisplayText = VkToString(virtualKey);
        binding.Modifiers = 0;

        _hotkeyRepo.SaveBindings(profile.Id, profile.Bindings);
        Log.Information("Hotkey saved: {Action} => VK {Key}", actionName, virtualKey);
    }

    private static string VkToString(uint vk)
    {
        return vk switch
        {
            0x08 => "Backspace", 0x09 => "Tab",       0x0D => "Enter",
            0x10 => "Shift",     0x11 => "Ctrl",      0x12 => "Alt",
            0x13 => "Pause",     0x1B => "Esc",       0x20 => "Space",
            0x21 => "PageUp",    0x22 => "PageDown",  0x23 => "End",
            0x24 => "Home",      0x25 => "←",         0x26 => "↑",
            0x27 => "→",         0x28 => "↓",         0x2D => "Insert",
            0x2E => "Delete",
            0x30 => "0", 0x31 => "1", 0x32 => "2", 0x33 => "3", 0x34 => "4",
            0x35 => "5", 0x36 => "6", 0x37 => "7", 0x38 => "8", 0x39 => "9",
            0x41 => "A", 0x42 => "B", 0x43 => "C", 0x44 => "D", 0x45 => "E",
            0x46 => "F", 0x47 => "G", 0x48 => "H", 0x49 => "I", 0x4A => "J",
            0x4B => "K", 0x4C => "L", 0x4D => "M", 0x4E => "N", 0x4F => "O",
            0x50 => "P", 0x51 => "Q", 0x52 => "R", 0x53 => "S", 0x54 => "T",
            0x55 => "U", 0x56 => "V", 0x57 => "W", 0x58 => "X", 0x59 => "Y",
            0x5A => "Z",
            0x70 => "F1",  0x71 => "F2",  0x72 => "F3",  0x73 => "F4",
            0x74 => "F5",  0x75 => "F6",  0x76 => "F7",  0x77 => "F8",
            0x78 => "F9",  0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
            0xBA => ";",   0xBB => "=",   0xBC => ",",   0xBD => "-",
            0xBE => ".",   0xBF => "/",   0xC0 => "`",   0xDB => "[",
            0xDC => "\\",  0xDD => "]",   0xDE => "'",
            _ => $"VK{((int)vk)}"
        };
    }

    // ── Display ────────────────────────────────────────────────────

    private void LoadDisplaySettings()
    {
        SubtitleToggle.IsChecked = _isSubtitleEnabled;
        MinimapToggle.IsChecked = _isMiniMapEnabled;

        SubtitleToggle.Checked += (s, e) => _onSubtitleToggled?.Invoke(true);
        SubtitleToggle.Unchecked += (s, e) => _onSubtitleToggled?.Invoke(false);
        MinimapToggle.Checked += (s, e) => _onMinimapToggled?.Invoke(true);
        MinimapToggle.Unchecked += (s, e) => _onMinimapToggled?.Invoke(false);

        if (_getOpacity != null)
        {
            OpacitySlider.Value = _getOpacity();
            OpacityLabel.Text = $"当前: {_getOpacity() * 100:F0}%";
            OpacitySlider.ValueChanged += (s, e) =>
            {
                _setOpacity?.Invoke(e.NewValue);
                OpacityLabel.Text = $"当前: {e.NewValue * 100:F0}%";
            };
        }
    }
}

public sealed class HotkeyRow
{
    public string DisplayName { get; set; } = "";
    public string ActionName { get; set; } = "";
    public string CurrentBinding { get; set; } = "未绑定";
}
