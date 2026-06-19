using CommunityToolkit.Mvvm.Messaging;
using GuideAssistant.Controls;
using GuideAssistant.Helpers;
using GuideAssistant.Models;
using GuideAssistant.Services;
using GuideAssistant.ViewModels;
using GuideAssistant.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;
using System.Runtime.InteropServices;
using WinRT.Interop;

namespace GuideAssistant;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly TabManager _tabManager;
    private readonly WindowManager _windowManager;
    private readonly SubtitleService _subtitleService;
    private readonly DirectionService _directionService;
    private readonly GameDetector _gameDetector;
    private readonly HotkeyService _hotkeyService;

    private ToolbarWindow? _toolbarWindow;
    private SubtitleOverlay? _subtitleOverlay;
    private MiniMapOverlay? _miniMapOverlay;
    private IntPtr _hwnd;
    private System.Timers.Timer? _subtitleTimeSyncTimer;

    // WndProc subclass for WM_HOTKEY (RegisterHotKey → system-level hotkeys)
    private Win32Helper.WndProcDelegate? _originalWndProc;
    private Win32Helper.WndProcDelegate? _wndProcHook;

    public MainViewModel ViewModel => _viewModel;

    public MainWindow(
        MainViewModel viewModel, TabManager tabManager, WindowManager windowManager,
        SubtitleService subtitleService, DirectionService directionService,
        GameDetector gameDetector, HotkeyService hotkeyService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _tabManager = tabManager;
        _windowManager = windowManager;
        _subtitleService = subtitleService;
        _directionService = directionService;
        _gameDetector = gameDetector;
        _hotkeyService = hotkeyService;

        _hwnd = WindowNative.GetWindowHandle(this);
        _windowManager.MainWindowHandle = _hwnd;
        RootGrid.DataContext = _viewModel;

        Closed += MainWindow_Closed;

        InitializeWindow();
        InitializeWebView();
        _viewModel.InitializeHotkeys();
        _hotkeyService.RegisterSystemHotkeys(_hwnd);
        SubclassWindowForHotkey();
        _viewModel.InitializeSubtitleSync();
        RestoreWindowState();
        _gameDetector.Start();

        RegisterMessengerHandlers();

        try
        {
            _toolbarWindow = App.Services.GetRequiredService<ToolbarWindow>();
            _toolbarWindow.Activate();
            ConnectToolbarViewModel();
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create ToolbarWindow"); }
    }

    private void SubclassWindowForHotkey()
    {
        _wndProcHook = OnWndProc;
        var hookPtr = Marshal.GetFunctionPointerForDelegate(_wndProcHook);
        var originalPtr = Win32Helper.SetWindowLongPtr(_hwnd, Win32Helper.GWLP_WNDPROC, hookPtr);
        _originalWndProc = Marshal.GetDelegateForFunctionPointer<Win32Helper.WndProcDelegate>(originalPtr);
    }

    private IntPtr OnWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        const uint WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY)
        {
            if (HotkeyService.SuppressAll)
                return IntPtr.Zero;

            int id = wParam.ToInt32();
            _hotkeyService.HandleSystemHotkey(id);
            return IntPtr.Zero;
        }

        return _originalWndProc?.Invoke(hwnd, msg, wParam, lParam)
               ?? Win32Helper.DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void InitializeWindow()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveForegroundColor = Colors.Transparent;
        AppWindow.Title = "";
        titleBar.SetDragRectangles(new Windows.Graphics.RectInt32[]
        {
            new Windows.Graphics.RectInt32 { X = 0, Y = 0, Width = 100000, Height = 48 }
        });
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null) presenter.IsAlwaysOnTop = true;
        AppWindow.Show();
        Log.Information("MainWindow initialized");
    }

    private void OpacityBtn_Click(object sender, RoutedEventArgs e)
    {
        OpacitySlider.Visibility = OpacitySlider.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void InitializeWebView()
    {
        WebViewControl.Initialize(_tabManager);
        WebViewControl.TitleChanged += title => _viewModel.CurrentTitle = title;
        WebViewControl.UrlChanged += url =>
        {
            _viewModel.CurrentUrl = url;
            _viewModel.UpdateBookmarkState(url);
            if (url.Contains("bilibili.com/video/"))
                _ = _subtitleService.LoadSubtitle(url);
        };
        WebViewControl.LoadingStateChanged += isLoading => _viewModel.IsLoading = isLoading;
        if (_tabManager.ActiveTab != null) LoadTab(_tabManager.ActiveTab);
    }

    private void LoadTab(TabItem tab) => WebViewControl.LoadUrl(tab, tab.Url);

    // ── Messenger Handlers ──────────────────────────────

    private void RegisterMessengerHandlers()
    {
        WeakReferenceMessenger.Default.Register<WebViewNavigateMessage>(this, (r, m) =>
            WebViewControl.Navigate(m.Url));

        WeakReferenceMessenger.Default.Register<WebViewActionMessage>(this, (r, m) =>
        {
            switch (m.Action)
            {
                case WebViewAction.GoBack: WebViewControl.GoBack(); break;
                case WebViewAction.GoForward: WebViewControl.GoForward(); break;
                case WebViewAction.Refresh: WebViewControl.Refresh(); break;
            }
        });

        WeakReferenceMessenger.Default.Register<SwitchTabMessage>(this, (r, m) => LoadTab(m.Tab));
        WeakReferenceMessenger.Default.Register<TabClosedMessage>(this, (r, m) => WebViewControl.RemoveWebView(m.TabId));

        WeakReferenceMessenger.Default.Register<VisibilityChangedMessage>(this, (r, m) =>
        { if (m.IsVisible) AppWindow.Show(); else AppWindow.Hide(); });

        WeakReferenceMessenger.Default.Register<ExecuteScriptRequestMessage>(this, async (r, m) =>
        {
            var result = await WebViewControl.ExecuteScript(m.Script);
            m.Tcs.SetResult(result);
        });

        WeakReferenceMessenger.Default.Register<OpacityChangedMessage>(this, (r, m) =>
            _windowManager.SetOpacity(_hwnd, m.Value));

        WeakReferenceMessenger.Default.Register<OverlayToggleMessage>(this, (r, m) =>
        {
            if (m.Type == "subtitle")
            {
                if (m.Enabled) { _subtitleOverlay = new SubtitleOverlay(_subtitleService); _subtitleOverlay.Activate(); _subtitleService.StartSync(); }
                else { _subtitleService.StopSync(); _subtitleOverlay?.Close(); _subtitleOverlay = null; }
            }
            else if (m.Type == "minimap")
            {
                if (m.Enabled) { _miniMapOverlay = new MiniMapOverlay(_directionService); _miniMapOverlay.Activate(); }
                else { _miniMapOverlay?.Close(); _miniMapOverlay = null; }
            }
        });

        WeakReferenceMessenger.Default.Register<SubtitleSyncMessage>(this, (r, m) =>
        { if (m.Start) StartSubtitleTimeSync(); else StopSubtitleTimeSync(); });

        WeakReferenceMessenger.Default.Register<HotkeysReloadMessage>(this, (r, m) =>
        {
            _viewModel.ReloadHotkeys();
            _hotkeyService.RegisterSystemHotkeys(_hwnd);
        });

        WeakReferenceMessenger.Default.Register<OpenSettingsMessage>(this, (r, m) =>
            OpenSettingsWindow());
    }

    private void ConnectToolbarViewModel()
    {
        var toolbarVm = App.Services.GetRequiredService<ToolbarViewModel>();
        toolbarVm.NavigateToUrlRequested += url =>
        {
            if (_tabManager.ActiveTab != null)
            {
                LoadTab(_tabManager.ActiveTab);
                _tabManager.Navigate(_tabManager.ActiveTab, url);
                WebViewControl.Navigate(url);
            }
        };
        toolbarVm.WebViewActionRequested += action =>
        {
            switch (action)
            {
                case WebViewAction.GoBack: WebViewControl.GoBack(); break;
                case WebViewAction.GoForward: WebViewControl.GoForward(); break;
                case WebViewAction.Refresh: WebViewControl.Refresh(); break;
            }
        };
        toolbarVm.CloseTabRequested += tabId => WebViewControl.RemoveWebView(tabId);
        toolbarVm.TabSwitched += tab => LoadTab(tab);
        toolbarVm.SettingsRequested += OpenSettingsWindow;
    }

    private void OpenSettingsWindow()
    {
        var settingsVm = App.Services.GetRequiredService<SettingsViewModel>();
        settingsVm.HotkeysReloaded += () => WeakReferenceMessenger.Default.Send(new HotkeysReloadMessage());
        settingsVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsSubtitleEnabled))
                _viewModel.IsSubtitleEnabled = settingsVm.IsSubtitleEnabled;
            else if (e.PropertyName == nameof(SettingsViewModel.IsMiniMapEnabled))
                _viewModel.IsMiniMapEnabled = settingsVm.IsMiniMapEnabled;
            else if (e.PropertyName == nameof(SettingsViewModel.Opacity))
                WeakReferenceMessenger.Default.Send(new OpacityChangedMessage(settingsVm.Opacity));
        };
        var settingsWindow = new SettingsWindow(settingsVm);
        settingsWindow.Activate();
    }

    // ── Subtitle Time Sync ──────────────────────────────

    private void StartSubtitleTimeSync()
    {
        _subtitleTimeSyncTimer?.Dispose();
        _subtitleTimeSyncTimer = new System.Timers.Timer(500) { AutoReset = true };
        _subtitleTimeSyncTimer.Elapsed += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var json = await WebViewControl.ExecuteScript("window.__gv_player.getTime()");
                    if (!string.IsNullOrEmpty(json))
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("current", out var current))
                            _subtitleService.UpdateTime(current.GetDouble());
                    }
                }
                catch { }
            });
        };
        _subtitleTimeSyncTimer.Start();
    }

    private void StopSubtitleTimeSync() { _subtitleTimeSyncTimer?.Dispose(); _subtitleTimeSyncTimer = null; }

    // ── Window State ────────────────────────────────────

    private void RestoreWindowState()
    {
        var state = _viewModel.LoadWindowState("MainWindow");
        if (state == null || AppWindow == null) return;
        try
        {
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32
            {
                X = (int)state.X, Y = (int)state.Y,
                Width = (int)state.Width, Height = (int)state.Height
            });
            _windowManager.SetOpacity(_hwnd, state.Opacity);
            _viewModel.Opacity = state.Opacity;
        }
        catch (Exception ex) { Log.Warning(ex, "Restore window state failed"); }
    }

    private void SaveWindowState()
    {
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        _viewModel.SaveWindowState(pos.X, pos.Y, size.Width, size.Height);
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveWindowState();
        StopSubtitleTimeSync();
        _subtitleOverlay?.Close();
        _miniMapOverlay?.Close();
        _toolbarWindow?.Close();
        _viewModel.Cleanup();
        _gameDetector.Dispose();
        Log.Information("Application shutting down");
    }
}
