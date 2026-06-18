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
    private static bool _webViewMissingShown;
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
            if (!_webViewMissingShown)
            {
                _webViewMissingShown = true;
                Log.Error(ex, "WebView2 runtime not installed");
                Application.Current.Exit();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load URL: {Url}", url);
        }
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

        // Prevent new window popups — navigate in same WebView
        wv.CoreWebView2.NewWindowRequested += (s, e) =>
        {
            e.Handled = true;
            wv.CoreWebView2.Navigate(e.Uri);
        };

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
