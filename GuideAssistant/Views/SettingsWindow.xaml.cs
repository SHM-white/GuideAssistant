using CommunityToolkit.Mvvm.Messaging;
using GuideAssistant.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Serilog;

namespace GuideAssistant.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        SettingsNav.DataContext = _viewModel;

        SetupWindow();
        _viewModel.LoadHotkeys();
        HotkeyList.ItemsSource = _viewModel.HotkeyRows;
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

    // ── Navigation ───────────────────────────────────────

    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            HotkeysPage.Visibility = Visibility.Collapsed;
            DisplayPage.Visibility = Visibility.Collapsed;
            AboutPage.Visibility = Visibility.Collapsed;

            switch (item.Tag?.ToString())
            {
                case "hotkeys":
                    HotkeysPage.Visibility = Visibility.Visible;
                    _viewModel.SelectPageCommand.Execute("hotkeys");
                    break;
                case "display":
                    DisplayPage.Visibility = Visibility.Visible;
                    _viewModel.SelectPageCommand.Execute("display");
                    break;
                case "about":
                    AboutPage.Visibility = Visibility.Visible;
                    _viewModel.SelectPageCommand.Execute("about");
                    break;
            }
        }
    }

    // ── Hotkeys ──────────────────────────────────────────

    private void HotkeyRebind_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton btn || btn.DataContext is not HotkeyRow row)
            return;

        _viewModel.StartKeyCaptureCommand.Execute(row);
        KeyCaptureTitle.Text = _viewModel.KeyCaptureTitle;
        KeyCaptureOverlay.Visibility = Visibility.Visible;
        KeyCaptureOverlay.Focus(FocusState.Keyboard);
    }

    private void KeyCaptureOverlay_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        e.Handled = true;

        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _viewModel.CancelKeyCapture();
            KeyCaptureOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        uint vk = (uint)(int)e.Key;
        KeyCaptureOverlay.Visibility = Visibility.Collapsed;
        _viewModel.OnKeyCaptured(vk);

        HotkeyList.ItemsSource = null;
        HotkeyList.ItemsSource = _viewModel.HotkeyRows;
    }

    private void HotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is HotkeyRow row)
        {
            _viewModel.ClearHotkeyCommand.Execute(row);
            HotkeyList.ItemsSource = null;
            HotkeyList.ItemsSource = _viewModel.HotkeyRows;
        }
    }

    // ── Display ──────────────────────────────────────────

    private void LoadDisplaySettings()
    {
        SubtitleToggle.IsChecked = _viewModel.IsSubtitleEnabled;
        MinimapToggle.IsChecked = _viewModel.IsMiniMapEnabled;

        SubtitleToggle.Checked += (s, e) => _viewModel.IsSubtitleEnabled = true;
        SubtitleToggle.Unchecked += (s, e) => _viewModel.IsSubtitleEnabled = false;
        MinimapToggle.Checked += (s, e) => _viewModel.IsMiniMapEnabled = true;
        MinimapToggle.Unchecked += (s, e) => _viewModel.IsMiniMapEnabled = false;

        OpacitySlider.Value = _viewModel.Opacity;
        OpacityLabel.Text = _viewModel.OpacityLabel;
        OpacitySlider.ValueChanged += (s, e) =>
        {
            _viewModel.Opacity = e.NewValue;
            OpacityLabel.Text = _viewModel.OpacityLabel;
        };
    }
}
