using GuideAssistant.Models;
using GuideAssistant.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Serilog;
using GuideAssistant.Helpers;
using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GuideAssistant.Controls;

public sealed partial class WebViewContainer : UserControl
{
    private Microsoft.UI.Xaml.Controls.WebView2? _webView;
    private TabManager? _tabManager;

    public event Action<string>? TitleChanged;
    public event Action<string>? UrlChanged;
    public event Action<bool>? LoadingStateChanged;

    public WebViewContainer()
    {
        InitializeComponent();
    }

    public void Initialize(TabManager tabManager)
    {
        _tabManager = tabManager;
    }

    public void LoadUrl(TabItem tab, string url)
    {
        if (_tabManager == null) return;

        try
        {
            var wv = _tabManager.GetOrCreateWebView(tab, t =>
            {
                var webView2 = new Microsoft.UI.Xaml.Controls.WebView2();
                WebViewHost.Children.Clear();
                WebViewHost.Children.Add(webView2);

                webView2.EnsureCoreWebView2Async().Completed += (s, e) =>
                {
                    if (webView2.CoreWebView2 != null)
                        SetupWebView(webView2, t);
                };
                webView2.Source = new Uri(url);
                return webView2;
            });

            _webView = wv;

            // Ensure the current active WebView is displayed
            if (!WebViewHost.Children.Contains(wv))
            {
                WebViewHost.Children.Clear();
                WebViewHost.Children.Add(wv);
                wv.Visibility = Visibility.Visible;
            }
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x800F1000))
        {
            Log.Error(ex, "WebView2 runtime is not installed");
            _ = ShowWebView2MissingDialogAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load URL: {Url}", url);
        }
    }

    private async Task ShowWebView2MissingDialogAsync()
    {
        // Wait for the control to be in the visual tree
        while (XamlRoot == null)
            await Task.Delay(100);

        var dialog = new ContentDialog
        {
            Title = "WebView2 运行时未安装",
            Content = "GuideAssistant 需要 WebView2 运行时才能显示网页内容。\n\n请点击下方按钮下载并安装 WebView2 运行时，然后重新启动应用。",
            PrimaryButtonText = "下载 WebView2 运行时",
            CloseButtonText = "关闭应用",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open WebView2 download link");
            }
        }

        // Close the application regardless of choice
        Application.Current.Exit();
    }

    private void SetupWebView(Microsoft.UI.Xaml.Controls.WebView2 wv, TabItem tab)
    {
        var settings = wv.CoreWebView2.Settings;
        settings.IsScriptEnabled = true;
        settings.IsWebMessageEnabled = true;
        settings.AreDefaultScriptDialogsEnabled = true;
        settings.IsPasswordAutosaveEnabled = false;
        settings.IsGeneralAutofillEnabled = false;

        // Inject fullscreen hijack script
        _ = wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(
            ScriptInjector.DefaultFullscreenHijack);

        // Inject player controls
        var playerScript = @"
window.__gv_player = {
    togglePlay: function() {
        var v = document.querySelector('video') || document.querySelector('bpx-player video');
        if (!v) return 'no video';
        if (v.paused) { v.play(); return 'play'; } else { v.pause(); return 'pause'; }
    },
    fastForward: function(s) {
        var v = document.querySelector('video') || document.querySelector('bpx-player video');
        if (v) v.currentTime = Math.min(v.duration||0, v.currentTime + (s||10));
    },
    fastBackward: function(s) {
        var v = document.querySelector('video') || document.querySelector('bpx-player video');
        if (v) v.currentTime = Math.max(0, v.currentTime - (s||10));
    },
    volumeUp: function() {
        var v = document.querySelector('video') || document.querySelector('bpx-player video');
        if (v) v.volume = Math.min(1, v.volume + 0.1);
    },
    volumeDown: function() {
        var v = document.querySelector('video') || document.querySelector('bpx-player video');
        if (v) v.volume = Math.max(0, v.volume - 0.1);
    },
    getTime: function() {
        var v = document.querySelector('video') || document.querySelector('bpx-player video');
        return v ? { current: v.currentTime, duration: v.duration||0, paused: v.paused } : null;
    }
};";
        _ = wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(playerScript);

        // Set up container attribute for CSS
        wv.DefaultBackgroundColor = Microsoft.UI.Colors.Black;

        // Events
        wv.CoreWebView2.DocumentTitleChanged += (s, e) =>
        {
            var title = wv.CoreWebView2.DocumentTitle;
            tab.Title = string.IsNullOrEmpty(title) ? "新标签页" : title;
            TitleChanged?.Invoke(tab.Title);
        };

        wv.CoreWebView2.SourceChanged += (s, e) =>
        {
            var url = wv.Source?.ToString() ?? "";
            tab.Url = url;
            UrlChanged?.Invoke(url);
        };

        wv.CoreWebView2.NavigationStarting += (s, e) =>
        {
            tab.IsLoading = true;
            LoadingBar.Visibility = Visibility.Visible;
            LoadingBar.IsIndeterminate = true;
            LoadingStateChanged?.Invoke(true);
        };

        wv.CoreWebView2.NavigationCompleted += (s, e) =>
        {
            tab.IsLoading = false;
            LoadingBar.Visibility = Visibility.Collapsed;
            LoadingStateChanged?.Invoke(false);

            tab.CanGoBack = wv.CoreWebView2.CanGoBack;
            tab.CanGoForward = wv.CoreWebView2.CanGoForward;
        };

        Log.Information("WebView2 initialized for tab: {Id}", tab.Id);
    }

    public async Task<string> ExecuteScript(string script)
    {
        if (_webView == null) return "";
        try
        {
            return await _webView.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ExecuteScript failed");
            return "";
        }
    }

    public void Navigate(string url)
    {
        if (_webView?.CoreWebView2 != null && Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _webView.CoreWebView2.Navigate(url);
        }
    }

    public void GoBack()
    {
        _webView?.CoreWebView2?.GoBack();
    }

    public void GoForward()
    {
        _webView?.CoreWebView2?.GoForward();
    }

    public void Refresh()
    {
        _webView?.CoreWebView2?.Reload();
    }
}
