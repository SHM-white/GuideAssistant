using CommunityToolkit.Mvvm.Messaging;
using GuideAssistant.Services;
using GuideAssistant.Data;
using Microsoft.Extensions.DependencyInjection;
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
    private int? _pendingKey;
    private int _pendingModifiers;
    private int? _lastModifierKey;
    private bool _refreshingScopes;
    private readonly GameDetector _gameDetector;
    private readonly GameConfigPage _gamesPage;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _gameDetector = App.Services.GetRequiredService<GameDetector>();
        _gamesPage = new GameConfigPage(
            App.Services.GetRequiredService<GameRepository>(),
            _gameDetector,
            App.Services.GetRequiredService<HotkeyConfigManager>());
        _gamesPage.GamesChanged += GamesPage_GamesChanged;
        GamesPage.Content = _gamesPage;
        SettingsNav.DataContext = _viewModel;

        SetupWindow();
        RefreshHotkeyScopeOptions();
        HotkeyList.ItemsSource = _viewModel.HotkeyRows;
        LoadDisplaySettings();
        VersionText.Text = Services.VersionInfo.GetDisplayVersion();

        SettingsNav.SelectedItem = SettingsNav.MenuItems[0];

        // Safety: ensure SuppressAll is cleared when window closes
        Closed += (s, e) =>
        {
            _gamesPage.GamesChanged -= GamesPage_GamesChanged;
            _viewModel.Dispose();
        };
    }

    private void SetupWindow()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveForegroundColor = Colors.Transparent;

        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 660, Height = 600 });
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
            GamesPage.Visibility = Visibility.Collapsed;
            DisplayPage.Visibility = Visibility.Collapsed;
            AboutPage.Visibility = Visibility.Collapsed;

            switch (item.Tag?.ToString())
            {
                case "hotkeys":
                    HotkeysPage.Visibility = Visibility.Visible;
                    RefreshHotkeyScopeOptions();
                    _viewModel.SelectPageCommand.Execute("hotkeys");
                    break;
                case "games":
                    GamesPage.Visibility = Visibility.Visible;
                    _viewModel.SelectPageCommand.Execute("games");
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

    private void GamesPage_GamesChanged()
    {
        _gameDetector.ReloadConfigs();
        RefreshHotkeyScopeOptions();
    }

    private void RefreshHotkeyScopeOptions()
    {
        _refreshingScopes = true;
        try
        {
            _viewModel.RefreshScopes();
            HotkeyScopeSelector.ItemsSource = _viewModel.HotkeyScopes;
            HotkeyScopeSelector.SelectedItem = _viewModel.HotkeyScopes.FirstOrDefault(
                x => x.GameConfigId == _viewModel.EditingGameConfigId);
        }
        finally
        {
            _refreshingScopes = false;
        }
        UpdateHotkeyScopeUi();
    }

    private void HotkeyScope_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingScopes) return;
        var item = HotkeyScopeSelector.SelectedItem as HotkeyScopeOption;
        _viewModel.SelectScope(item?.GameConfigId);
        UpdateHotkeyScopeUi();
    }

    private void ResetGameHotkeys_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetSelectedGameProfile();
        UpdateHotkeyScopeUi();
    }

    private void UpdateHotkeyScopeUi()
    {
        ResetGameHotkeysButton.IsEnabled = _viewModel.EditingGameConfigId is not null;
        HotkeyScopeHint.Text = _viewModel.ScopeDescription;
    }

    // ── Hotkeys ──────────────────────────────────────────

    private async void HotkeyRebind_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not HyperlinkButton btn || btn.DataContext is not HotkeyRow row)
            return;

        _viewModel.StartKeyCaptureCommand.Execute(row);
        KeyCaptureDialog.Title = _viewModel.KeyCaptureTitle;
        _pendingKey = null;
        _pendingModifiers = 0;
        _lastModifierKey = null;

        KeyCaptureDialog.Opened += OnKeyCaptureDialogOpened;
        await KeyCaptureDialog.ShowAsync();
        KeyCaptureDialog.Opened -= OnKeyCaptureDialogOpened;

        if (_pendingKey.HasValue)
            _viewModel.OnKeyCaptured(_pendingKey.Value, _pendingModifiers);
        else
            _viewModel.CancelKeyCapture();
        UpdateHotkeyScopeUi();
    }

    private void OnKeyCaptureDialogOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        KeyCaptureContent.Focus(FocusState.Keyboard);
    }

    private void OnKeyCaptureKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
            return; // Let ContentDialog handle ESC via its CloseButton

        e.Handled = true;
        var key = (int)e.Key;
        if (HotkeyService.IsModifierKey(key))
        {
            // Release the modifier alone to bind it as a single key.
            _lastModifierKey = key;
            return;
        }

        _pendingKey = key;
        _pendingModifiers = HotkeyService.ReadCurrentModifiers();
        KeyCaptureDialog.Hide();
    }

    private void OnKeyCaptureKeyUp(object sender, KeyRoutedEventArgs e)
    {
        if (_pendingKey.HasValue || _lastModifierKey != (int)e.Key)
            return;

        e.Handled = true;
        _pendingKey = (int)e.Key;
        _pendingModifiers = 0;
        KeyCaptureDialog.Hide();
    }

    private void HotkeyClear_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is HotkeyRow row)
        {
            _viewModel.ClearHotkeyCommand.Execute(row);
            UpdateHotkeyScopeUi();
        }
    }

    // ── Display ──────────────────────────────────────────

    private void LoadDisplaySettings()
    {
        SubtitleToggle.IsChecked = _viewModel.IsSubtitleEnabled;
        MinimapToggle.IsChecked = _viewModel.IsMiniMapEnabled;
        MinimapFilterToggle.IsChecked = _viewModel.IsMinimapFilterEnabled;

        SubtitleToggle.Checked += (s, e) => _viewModel.IsSubtitleEnabled = true;
        SubtitleToggle.Unchecked += (s, e) => _viewModel.IsSubtitleEnabled = false;
        MinimapToggle.Checked += (s, e) => _viewModel.IsMiniMapEnabled = true;
        MinimapToggle.Unchecked += (s, e) => _viewModel.IsMiniMapEnabled = false;
        MinimapFilterToggle.Checked += (s, e) => _viewModel.IsMinimapFilterEnabled = true;
        MinimapFilterToggle.Unchecked += (s, e) => _viewModel.IsMinimapFilterEnabled = false;

        OpacitySlider.Value = _viewModel.Opacity;
        OpacityLabel.Text = _viewModel.OpacityLabel;
        OpacitySlider.ValueChanged += (s, e) =>
        {
            _viewModel.Opacity = e.NewValue;
            OpacityLabel.Text = _viewModel.OpacityLabel;
        };
    }
}
