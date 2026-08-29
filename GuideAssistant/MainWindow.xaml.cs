using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Input;
using GuideAssistant.Controls;
using GuideAssistant.Helpers;
using GuideAssistant.Models;
using GuideAssistant.Services;
using GuideAssistant.Overlays;
using GuideAssistant.ViewModels;
using GuideAssistant.Views;
using H.NotifyIcon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;
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
    private readonly HotkeyConfigManager _hotkeyConfigManager;

    private ToolbarWindow? _toolbarWindow;
    private readonly IOverlayController _overlayCtrl;
    private readonly AudioCaptureService _audioCapture;
    private readonly SpeechRecognitionService _speechRecognition;
    private readonly BilibiliApi _bilibiliApi;
    private ToolbarViewModel? _toolbarVM;
    private WebView2AudioBridge? _audioBridge;
    private Microsoft.Web.WebView2.Core.CoreWebView2? _webMessageCoreWebView;
    private SettingsWindow? _settingsWindow;
    private TaskbarIcon? _trayIcon;
    private IntPtr _hwnd;
    private System.Timers.Timer? _subtitleTimeSyncTimer;
    private int _bilibiliNavigationVersion;

    public MainViewModel ViewModel => _viewModel;

    public MainWindow(
        MainViewModel viewModel, TabManager tabManager, WindowManager windowManager,
        SubtitleService subtitleService, DirectionService directionService,
        GameDetector gameDetector, HotkeyService hotkeyService, HotkeyConfigManager hotkeyConfigManager,
        IOverlayController overlayCtrl, AudioCaptureService audioCapture, SpeechRecognitionService speechRecognition,
        BilibiliApi bilibiliApi)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _tabManager = tabManager;
        _windowManager = windowManager;
        _subtitleService = subtitleService;
        _directionService = directionService;
        _gameDetector = gameDetector;
        _hotkeyService = hotkeyService;
        _hotkeyConfigManager = hotkeyConfigManager;
        _overlayCtrl = overlayCtrl;
        _audioCapture = audioCapture;
        _speechRecognition = speechRecognition;
        _bilibiliApi = bilibiliApi;

        // React to hotkey binding changes (from Settings or elsewhere)
        _hotkeyConfigManager.BindingsChanged += () =>
        {
            _viewModel.ReloadHotkeys();
        };

        _hwnd = WindowNative.GetWindowHandle(this);
        _windowManager.MainWindowHandle = _hwnd;
        RootGrid.DataContext = _viewModel;

        Closed += MainWindow_Closed;

        InitializeWindow();
        InitializeWebView();
        _viewModel.InitializeHotkeys();
        _viewModel.InitializeSubtitleSync();
        RestoreWindowState();
        _gameDetector.Start();

        RegisterMessengerHandlers();
        InitializeTrayIcon();

        try
        {
            _toolbarWindow = App.Services.GetRequiredService<ToolbarWindow>();
            _toolbarWindow.Activate();
            ConnectToolbarViewModel();
        }
        catch (Exception ex) { Log.Error(ex, "Failed to create ToolbarWindow"); }
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

    private void RestoreToolsBtn_Click(object sender, RoutedEventArgs e)
    {
        ShowToolbarWindow();

        if (_viewModel.IsSubtitleEnabled && !_overlayCtrl.IsSubtitleVisible)
        {
            _overlayCtrl.ShowSubtitle();
            StartSubtitleTimeSync();
        }
        if (_viewModel.IsMiniMapEnabled && !_overlayCtrl.IsMiniMapVisible)
        {
            _overlayCtrl.ShowMiniMap();
        }
    }

    private void ShowToolbarWindow()
    {
        try
        {
            if (_toolbarWindow is null)
            {
                _toolbarWindow = App.Services.GetRequiredService<ToolbarWindow>();
                _toolbarWindow.Closed += (s, e) => _toolbarWindow = null;
                _toolbarWindow.Activate();
                ConnectToolbarViewModel();
            }
            else
            {
                _toolbarWindow.AppWindow.Show();
            }
        }
        catch (Exception ex) { Log.Error(ex, "Failed to show ToolbarWindow"); }
    }

    private void InitializeWebView()
    {
        WebViewControl.Initialize(_tabManager);
        WebViewControl.TitleChanged += title => _viewModel.CurrentTitle = title;
        WebViewControl.UrlChanged += url =>
        {
            var navigationVersion = System.Threading.Interlocked.Increment(ref _bilibiliNavigationVersion);
            _viewModel.CurrentUrl = url;
            _viewModel.UpdateBookmarkState(url);

            // Update toolbar address bar for every navigation
            _toolbarVM?.OnUrlChanged(url);

            if (url.Contains("bilibili.com/video/"))
            {
                _ = HandleBilibiliVideoNavigationAsync(url, navigationVersion);
            }
        };
        WebViewControl.LoadingStateChanged += isLoading => _viewModel.IsLoading = isLoading;
        _subtitleService.SubtitleChanged += text =>
        {
            Log.Information("MainWindow subtitle received: \"{Text}\" (overlay visible={Vis})", text, _overlayCtrl.IsSubtitleVisible);
            DispatcherQueue.TryEnqueue(() => _overlayCtrl.UpdateSubtitle(text));
        };
        if (_tabManager.ActiveTab != null) LoadTab(_tabManager.ActiveTab);
    }

    private void LoadTab(TabItem tab) => WebViewControl.LoadUrl(tab, tab.Url);

    private async Task HandleBilibiliVideoNavigationAsync(string url, int navigationVersion)
    {
        var bvid = BilibiliApi.ExtractBvid(url);
        Log.Information("MainWindow: B站 video detected, url={Url} bvid={Bvid}", url, bvid);

        var wv = WebViewControl.CurrentCoreWebView2;
        _audioBridge?.Dispose();
        _audioBridge = null;

        if (wv != null)
        {
            var bridge = new WebView2AudioBridge(wv, _audioCapture);
            _audioBridge = bridge;

            try
            {
                await bridge.InitializeAsync();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "MainWindow: WebView2 audio bridge initialization failed");
            }

            if (navigationVersion != System.Threading.Volatile.Read(ref _bilibiliNavigationVersion) || !ReferenceEquals(_audioBridge, bridge) || !ReferenceEquals(WebViewControl.CurrentCoreWebView2, wv))
            {
                bridge.Dispose();
                return;
            }

            // Extract B站 cookies for API authentication
            _ = ExtractBilibiliCookiesAsync(wv);

            AttachWebMessageHandler(wv);
        }

        if (navigationVersion != System.Threading.Volatile.Read(ref _bilibiliNavigationVersion))
        {
            return;
        }

        _subtitleService.SetCoreWebView(wv);
        await _subtitleService.LoadSubtitle(url);
    }

    private void AttachWebMessageHandler(Microsoft.Web.WebView2.Core.CoreWebView2 wv)
    {
        if (_webMessageCoreWebView != null)
        {
            _webMessageCoreWebView.WebMessageReceived -= OnWebMessageReceived;
        }

        wv.WebMessageReceived -= OnWebMessageReceived;
        wv.WebMessageReceived += OnWebMessageReceived;
        _webMessageCoreWebView = wv;
    }

    private void OnWebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs msg)
    {
        var txt = msg.TryGetWebMessageAsString();
        if (string.IsNullOrEmpty(txt)) return;
        if (txt.StartsWith("__gv_debug:"))
        {
            Log.Information("[WebView] {Msg}", txt);
        }
        else if (txt.StartsWith("__gv_subtitle_json:"))
        {
            // Format: __gv_subtitle_json:bvid:json_body
            var payload = txt["__gv_subtitle_json:".Length..];
            var sep = payload.IndexOf(':');
            if (sep > 0)
            {
                var bvid = payload[..sep];
                var json = payload[(sep + 1)..];
                BilibiliApi.CacheInterceptedSubtitle(bvid, json);
                // Replace provider data with intercepted (accurate) subtitle
                _ = _subtitleService.ReplaceWithInterceptedAsync(bvid, json);
            }
        }
    }

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
        {
            if (m.IsVisible)
                Win32Helper.ShowWindow(_hwnd, Win32Helper.SW_SHOW);
            else
                Win32Helper.ShowWindow(_hwnd, Win32Helper.SW_HIDE);
        });

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
                if (m.Enabled) { _overlayCtrl.ShowSubtitle(); StartSubtitleTimeSync(); }
                else { StopSubtitleTimeSync(); _overlayCtrl.HideSubtitle(); }
            }
            else if (m.Type == "minimap")
            {
                if (m.Enabled) { _overlayCtrl.ShowMiniMap(); }
                else { _overlayCtrl.HideMiniMap(); }
            }
        });

        WeakReferenceMessenger.Default.Register<DirectionWordMessage>(this, (r, m) =>
        {
            _overlayCtrl.ShowDirection(m.Word);
        });

        WeakReferenceMessenger.Default.Register<SubtitleSyncMessage>(this, (r, m) =>
        {
            if (m.Start) StartSubtitleTimeSync();
            else { StopSubtitleTimeSync(); _overlayCtrl.UpdateSubtitle(""); }
        });

        WeakReferenceMessenger.Default.Register<OpenSettingsMessage>(this, (r, m) =>
            OpenSettingsWindow());
    }

    private void ConnectToolbarViewModel()
    {
        _toolbarVM = App.Services.GetRequiredService<ToolbarViewModel>();
        _toolbarVM.NavigateToUrlRequested += url =>
        {
            if (_tabManager.ActiveTab != null)
            {
                LoadTab(_tabManager.ActiveTab);
                _tabManager.Navigate(_tabManager.ActiveTab, url);
                WebViewControl.Navigate(url);
            }
        };
        _toolbarVM.WebViewActionRequested += action =>
        {
            switch (action)
            {
                case WebViewAction.GoBack: WebViewControl.GoBack(); break;
                case WebViewAction.GoForward: WebViewControl.GoForward(); break;
                case WebViewAction.Refresh: WebViewControl.Refresh(); break;
            }
        };
        _toolbarVM.CloseTabRequested += tabId => WebViewControl.RemoveWebView(tabId);
        _toolbarVM.TabSwitched += tab => LoadTab(tab);
        _toolbarVM.SettingsRequested += OpenSettingsWindow;
    }

    private void OpenSettingsWindow()
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Activate();
            return;
        }

        var settingsVm = App.Services.GetRequiredService<SettingsViewModel>();
        settingsVm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(SettingsViewModel.IsSubtitleEnabled))
                _viewModel.IsSubtitleEnabled = settingsVm.IsSubtitleEnabled;
            else if (e.PropertyName == nameof(SettingsViewModel.IsMiniMapEnabled))
                _viewModel.IsMiniMapEnabled = settingsVm.IsMiniMapEnabled;
            else if (e.PropertyName == nameof(SettingsViewModel.IsMinimapFilterEnabled))
                _viewModel.IsMinimapFilterEnabled = settingsVm.IsMinimapFilterEnabled;
            else if (e.PropertyName == nameof(SettingsViewModel.Opacity))
                WeakReferenceMessenger.Default.Send(new OpacityChangedMessage(settingsVm.Opacity));
        };
        _settingsWindow = new SettingsWindow(settingsVm);
        _settingsWindow.Closed += (s, e) => _settingsWindow = null;
        _settingsWindow.Activate();
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
                catch (Exception ex) { Log.Debug(ex, "Subtitle sync tick failed"); }
            });
        };
        _subtitleTimeSyncTimer.Start();
    }

    private void StopSubtitleTimeSync() { _subtitleTimeSyncTimer?.Dispose(); _subtitleTimeSyncTimer = null; }

    // ── Tray Icon ──────────────────────────────────────

    private void InitializeTrayIcon()
    {
        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "GuideAssistant",
            IconSource = new GeneratedIconSource
            {
                Text = "GA",
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe UI"),
                FontSize = 28,
                Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.White),
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                    Windows.UI.Color.FromArgb(0xFF, 0x33, 0x88, 0xCC))
            },
            LeftClickCommand = new RelayCommand(() => ShowToolbarWindow()),
            RightClickCommand = new RelayCommand(() =>
            {
                var result = Win32Helper.ShowPopupMenu(_hwnd, new string?[] { "打开工具窗口", "打开设置", null, "退出应用" });
                switch (result)
                {
                    case 1:
                        ShowToolbarWindow();
                        break;
                    case 2:
                        OpenSettingsWindow();
                        break;
                    case 4:
                        App.Current.Exit();
                        break;
                }
            })
        };

        RootGrid.Children.Add(_trayIcon);
    }

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
        _overlayCtrl.HideSubtitle();
        _overlayCtrl.HideMiniMap();
        _overlayCtrl.Dispose();
        _toolbarWindow?.CloseForReal();
        _settingsWindow?.Close();
        _viewModel.Cleanup();
        _gameDetector.Dispose();
        _speechRecognition.StopAsync().GetAwaiter().GetResult();
        _speechRecognition.Dispose();
        _audioBridge?.Dispose();
        Log.Information("Application shutting down");
    }

    private async Task ExtractBilibiliCookiesAsync(Microsoft.Web.WebView2.Core.CoreWebView2 wv)
    {
        try
        {
            var cookieManager = wv.CookieManager;
            var cookies = await cookieManager.GetCookiesAsync("https://bilibili.com");
            var names = new[] { "SESSDATA", "bili_jct", "DedeUserID", "DedeUserID__ckMd5", "buvid3", "buvid4" };
            var parts = new List<string>();
            foreach (var c in cookies)
            {
                if (names.Contains(c.Name))
                    parts.Add($"{c.Name}={c.Value}");
            }
            _bilibiliApi.SetCookies(string.Join("; ", parts));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to extract B站 cookies");
        }
    }
}
