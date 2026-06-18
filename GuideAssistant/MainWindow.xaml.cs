using GuideAssistant.Controls;
using GuideAssistant.Data;
using GuideAssistant.Models;
using GuideAssistant.Services;
using GuideAssistant.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;
using WinRT.Interop;

namespace GuideAssistant;

public sealed partial class MainWindow : Window
{
    private readonly TabManager _tabManager;
    private readonly HotkeyService _hotkeyService;
    private readonly WindowManager _windowManager;
    private readonly GameDetector _gameDetector;
    private readonly BookmarkService _bookmarkService;
    private readonly SubtitleService _subtitleService;
    private readonly BilibiliApi _bilibiliApi;
    private readonly DirectionService _directionService;
    private readonly HotkeyRepository _hotkeyRepository;
    private readonly WindowStateRepository _windowStateRepo;

    private ToolbarWindow? _toolbarWindow;
    private SubtitleOverlay? _subtitleOverlay;
    private MiniMapOverlay? _miniMapOverlay;
    private IntPtr _hwnd;

    private bool _isVisible = true;
    private bool _isSubtitleEnabled;
    private bool _isMiniMapEnabled;

    // Events for ToolbarWindow to subscribe to
    public event Action<string>? UrlChanged;
    public event Action<string>? TitleChanged;
    public event Action<bool>? LoadingStateChanged;

    public MainWindow(
        TabManager tabManager,
        HotkeyService hotkeyService,
        WindowManager windowManager,
        GameDetector gameDetector,
        BookmarkService bookmarkService,
        SubtitleService subtitleService,
        BilibiliApi bilibiliApi,
        DirectionService directionService,
        HotkeyRepository hotkeyRepository,
        WindowStateRepository windowStateRepo)
    {
        InitializeComponent();

        _tabManager = tabManager;
        _hotkeyService = hotkeyService;
        _windowManager = windowManager;
        _gameDetector = gameDetector;
        _bookmarkService = bookmarkService;
        _subtitleService = subtitleService;
        _bilibiliApi = bilibiliApi;
        _directionService = directionService;
        _hotkeyRepository = hotkeyRepository;
        _windowStateRepo = windowStateRepo;

        _hwnd = WindowNative.GetWindowHandle(this);
        _windowManager.MainWindowHandle = _hwnd;

        Closed += MainWindow_Closed;

        InitializeWindow();
        InitializeControls();
        InitializeHotkeys();
        InitializeSubtitle();
        RestoreWindowState();

        // Create and show the ToolbarWindow
        try
        {
            _toolbarWindow = App.Services.GetRequiredService<ToolbarWindow>();
            _toolbarWindow.SetMainWindow(this);
            _toolbarWindow.Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create ToolbarWindow");
        }
    }

    private void InitializeWindow()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Standard;

        // Make system title bar elements fully transparent
        var transparent = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonBackgroundColor = transparent;
        titleBar.ButtonForegroundColor = transparent;
        titleBar.ButtonInactiveBackgroundColor = transparent;
        titleBar.ButtonInactiveForegroundColor = transparent;
        titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(40, 255, 255, 255);
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(60, 255, 255, 255);
        titleBar.ButtonPressedForegroundColor = Colors.White;

        // Remove system title text and icon
        AppWindow.Title = "";

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
            presenter.IsAlwaysOnTop = true;
        AppWindow.Show();
        Log.Information("MainWindow initialized");
    }

    private void InitializeControls()
    {
        TitleBarControl.MinimizeClicked += () =>
            (AppWindow.Presenter as OverlappedPresenter)?.Minimize();
        TitleBarControl.MaximizeClicked += () =>
        {
            var p = AppWindow.Presenter as OverlappedPresenter;
            if (p != null)
            {
                if (p.State == OverlappedPresenterState.Maximized) p.Restore();
                else p.Maximize();
            }
        };
        TitleBarControl.CloseClicked += () => Close();
        TitleBarControl.OpacitySliderControl.ValueChanged += (s, e) =>
            _windowManager.SetOpacity(_hwnd, e.NewValue);

        WebViewControl.Initialize(_tabManager);
        WebViewControl.TitleChanged += (title) => TitleChanged?.Invoke(title);
        WebViewControl.UrlChanged += (url) =>
        {
            UrlChanged?.Invoke(url);
            if (url.Contains("bilibili.com/video/"))
                _ = _subtitleService.LoadSubtitle(url);
        };
        WebViewControl.LoadingStateChanged += (isLoading) => LoadingStateChanged?.Invoke(isLoading);

        if (_tabManager.ActiveTab != null)
            SwitchToTab(_tabManager.ActiveTab);
    }

    private void SwitchToTab(TabItem tab)
    {
        WebViewControl.LoadUrl(tab, tab.Url);
        UrlChanged?.Invoke(tab.Url);
    }

    // --- Public API for ToolbarWindow ---

    public void NavigateToUrl(string url)
    {
        if (_tabManager.ActiveTab == null) return;
        _tabManager.Navigate(_tabManager.ActiveTab, url);
        WebViewControl.Navigate(url);
    }

    public void NavigateBack() => WebViewControl.GoBack();

    public void NavigateForward() => WebViewControl.GoForward();

    public void NavigateRefresh() => WebViewControl.Refresh();

    public string? GetCurrentUrl() => _tabManager.ActiveTab?.Url;

    public string? GetCurrentTitle() => _tabManager.ActiveTab?.Title;

    public bool IsCurrentUrlBookmarked()
    {
        var url = _tabManager.ActiveTab?.Url;
        return url != null && _bookmarkService.IsUrlBookmarked(url);
    }

    public async Task<string> ExecuteWebScript(string script)
        => await WebViewControl.ExecuteScript(script);

    public void AddBookmark()
    {
        var url = _tabManager.GetBookmarkUrl();
        var title = _tabManager.GetBookmarkTitle();
        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(title))
            _bookmarkService.QuickAdd(title, url);
    }

    public void ShowBookmarkSearch(string keyword)
    {
        var results = _bookmarkService.Search(keyword);
        if (results.Count > 0 && _tabManager.ActiveTab != null)
        {
            _tabManager.Navigate(_tabManager.ActiveTab, results[0].Url);
            WebViewControl.Navigate(results[0].Url);
        }
    }

    public void ShowSettingsPage()
    {
        var stack = new StackPanel { Spacing = 12, Padding = new Thickness(20) };
        stack.Children.Add(new TextBlock { Text = "快捷键", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(new TextBlock { Text = "` - 播放/暂停 | 5 - 快退 | 6 - 快进 | 8 - 音量- | 9 - 音量+" });
        stack.Children.Add(new TextBlock { Text = "S - 字幕切换 | M - 小地图切换" });
        stack.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(Colors.Gray), Margin = new Thickness(0, 4, 0, 4) });
        stack.Children.Add(new TextBlock { Text = "窗口控制", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        stack.Children.Add(new CheckBox { Content = "启用字幕覆盖", IsChecked = _isSubtitleEnabled });
        stack.Children.Add(new CheckBox { Content = "启用小地图覆盖", IsChecked = _isMiniMapEnabled });

        _ = new ContentDialog
        {
            Title = "设置",
            CloseButtonText = "关闭",
            XamlRoot = Content.XamlRoot,
            Content = stack
        }.ShowAsync();
    }

    private void InitializeHotkeys()
    {
        _hotkeyService.Initialize(_hwnd);

        var profile = _hotkeyRepository.GetDefaultProfile();
        if (profile?.Bindings.Count > 0)
        {
            foreach (var b in profile.Bindings)
                _hotkeyService.RegisterHotkey(b.ActionName, b.Modifiers, b.VirtualKey, () => HandleHotkeyAction(b.ActionName));
        }

        _hotkeyService.StartLowLevelHook((vkCode) =>
        {
            if (vkCode == 0xC0) HandleHotkeyAction("play_pause");
            else if (vkCode == 0x35) HandleHotkeyAction("fast_backward");
            else if (vkCode == 0x36) HandleHotkeyAction("fast_forward");
            else if (vkCode == 0x38) HandleHotkeyAction("volume_down");
            else if (vkCode == 0x39) HandleHotkeyAction("volume_up");
        });
    }

    private async void HandleHotkeyAction(string action)
    {
        switch (action)
        {
            case "play_pause":
                await WebViewControl.ExecuteScript("window.__gv_player.togglePlay()");
                break;
            case "fast_forward":
                await WebViewControl.ExecuteScript("window.__gv_player.fastForward(10)");
                break;
            case "fast_backward":
                await WebViewControl.ExecuteScript("window.__gv_player.fastBackward(10)");
                break;
            case "volume_up":
                await WebViewControl.ExecuteScript("window.__gv_player.volumeUp()");
                break;
            case "volume_down":
                await WebViewControl.ExecuteScript("window.__gv_player.volumeDown()");
                break;
            case "toggle_visibility":
                _isVisible = !_isVisible;
                if (_isVisible) AppWindow.Show(); else AppWindow.Hide();
                break;
            case "bookmark_page":
                var url = _tabManager.GetBookmarkUrl();
                var title = _tabManager.GetBookmarkTitle();
                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(title))
                    _bookmarkService.QuickAdd(title, url);
                break;
            case "toggle_subtitle":
                _isSubtitleEnabled = !_isSubtitleEnabled;
                ToggleSubtitleOverlay();
                break;
            case "toggle_minimap":
                _isMiniMapEnabled = !_isMiniMapEnabled;
                ToggleMiniMapOverlay();
                break;
        }
    }

    private void InitializeSubtitle()
    {
        _subtitleService.DirectionWordDetected += (word) =>
            _miniMapOverlay?.ShowDirection(word);
    }

    private void ToggleSubtitleOverlay()
    {
        if (_isSubtitleEnabled)
        {
            _subtitleOverlay = new SubtitleOverlay(_subtitleService);
            _subtitleOverlay.AppWindow.Show();
            _subtitleService.StartSync();
        }
        else
        {
            _subtitleService.StopSync();
            _subtitleOverlay?.Close();
            _subtitleOverlay = null;
        }
    }

    private void ToggleMiniMapOverlay()
    {
        if (_isMiniMapEnabled)
        {
            _miniMapOverlay = new MiniMapOverlay(_directionService);
            _miniMapOverlay.AppWindow.Show();
        }
        else
        {
            _miniMapOverlay?.Close();
            _miniMapOverlay = null;
        }
    }

    private void RestoreWindowState()
    {
        var state = _windowStateRepo.Get("MainWindow");
        if (state == null || AppWindow == null)
            return;

        try
        {
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32
            {
                X = (int)state.X, Y = (int)state.Y,
                Width = (int)state.Width, Height = (int)state.Height
            });
            _windowManager.SetOpacity(_hwnd, state.Opacity);
            TitleBarControl.OpacitySliderControl.Value = state.Opacity;
        }
        catch (Exception ex) { Log.Warning(ex, "Restore window state failed"); }
    }

    private void SaveWindowState()
    {
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        _windowStateRepo.Save(new WindowState
        {
            WindowName = "MainWindow",
            X = pos.X, Y = pos.Y,
            Width = size.Width, Height = size.Height,
            Opacity = TitleBarControl.OpacitySliderControl.Value,
            IsAlwaysOnTop = true
        });
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveWindowState();
        _hotkeyService.Dispose();
        _gameDetector.Dispose();
        _subtitleService.Dispose();
        _tabManager.Cleanup();
        Log.Information("Application shutting down");
    }
}
