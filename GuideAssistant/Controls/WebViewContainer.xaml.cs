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

    public CoreWebView2? CurrentCoreWebView2 => _webView?.CoreWebView2;

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
            var isNew = !_tabManager.HasWebView(tab.Id);
            var wv = _tabManager.GetOrCreateWebView(tab, t =>
            {
                var webView2 = new Microsoft.UI.Xaml.Controls.WebView2();
                webView2.Visibility = Visibility.Collapsed;
                WebViewHost.Children.Add(webView2);

                webView2.EnsureCoreWebView2Async().Completed += (s, e) =>
                {
                    if (webView2.CoreWebView2 != null)
                    {
                        SetupWebView(webView2, t);
                        if (!string.IsNullOrEmpty(t.Url))
                            webView2.CoreWebView2.Navigate(t.Url);
                    }
                };
                return webView2;
            });

            // Hide all WebViews, then show only the active one
            foreach (var child in WebViewHost.Children)
            {
                if (child is Microsoft.UI.Xaml.Controls.WebView2 childWv)
                    childWv.Visibility = Visibility.Collapsed;
            }
            wv.Visibility = Visibility.Visible;
            _webView = wv;

            // Navigate if CoreWebView2 is already ready and we're loading a different URL
            if (wv.CoreWebView2 != null && isNew)
            {
                wv.CoreWebView2.Navigate(url);
            }
            else if (wv.CoreWebView2 == null)
            {
                // CoreWebView2 not ready yet; update tab URL so completion handler uses it
                tab.Url = url;
            }
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x800F1000))
        {
            if (!_webViewMissingShown)
            {
                _webViewMissingShown = true;
                Log.Error(ex, "WebView2 runtime not installed");
                _ = DispatcherQueue.TryEnqueue(async () =>
                {
                    var dialog = new ContentDialog
                    {
                        Title = "缺少 WebView2 运行时",
                        Content = "请安装 Microsoft Edge WebView2 运行时后重新启动应用。\n下载地址: https://go.microsoft.com/fwlink/p/?LinkId=2124703",
                        CloseButtonText = "退出",
                        XamlRoot = this.XamlRoot
                    };
                    await dialog.ShowAsync();
                    Application.Current.Exit();
                });
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

        // Inject fetch interceptor to capture B站 subtitle data loaded by the player
        var fetchInterceptScript = @"
(function() {
  if (window.__gv_fetchIntercept) return;
  window.__gv_fetchIntercept = true;

  function extractBvid() {
    var m = location.href.match(/bilibili\.com\/video\/([A-Za-z0-9]+)/);
    return m ? m[1] : '';
  }

  function sendSubtitle(text, bvid) {
    try { chrome.webview.postMessage('__gv_subtitle_json:' + bvid + ':' + text); } catch(e) {}
  }

  var origFetch = window.fetch;
  window.fetch = function() {
    var url = arguments[0] && typeof arguments[0] === 'string' ? arguments[0] : (arguments[0] && arguments[0].url ? arguments[0].url : '');
    return origFetch.apply(this, arguments).then(function(response) {
      if (url.indexOf('aisubtitle.hdslb.com') !== -1) {
        try {
          var clone = response.clone();
          clone.text().then(function(text) {
            sendSubtitle(text, extractBvid());
          });
        } catch(e) {}
      }
      return response;
    });
  };

  var origOpen = XMLHttpRequest.prototype.open;
  XMLHttpRequest.prototype.open = function(method, url) {
    this.__gv_url = url;
    return origOpen.apply(this, arguments);
  };
  var origSend = XMLHttpRequest.prototype.send;
  XMLHttpRequest.prototype.send = function() {
    var self = this;
    var url = self.__gv_url || '';
    if (url.indexOf('aisubtitle.hdslb.com') !== -1) {
      self.addEventListener('load', function() {
        try { sendSubtitle(self.responseText, extractBvid()); } catch(e) {}
      });
    }
    return origSend.apply(this, arguments);
  };
})();";
        _ = wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(fetchInterceptScript);

        // Inject CC subtitle DOM observer (handles Shadow DOM + body fallback, with debug logging)
        var ccScript = @"
(function() {
  if (window.__gv_ccObserver) return;
  window.__gv_ccObserver = true;
  var lastText = '';
  var debugTick = 0;

  function getPlayerBounds() {
    var p = document.querySelector('bpx-player');
    if (!p) return null;
    return p.getBoundingClientRect();
  }

  function scanTree(root, plrRect) {
    var matched = [];
    var allCandidates = [];
    var treeWalker = document.createTreeWalker(root, NodeFilter.SHOW_ELEMENT, function(el) {
      var r = el.getBoundingClientRect();
      if (r.width < 40 || r.height < 10) return NodeFilter.FILTER_SKIP;
      var t = (el.textContent || '').trim();
      if (t.length <= 1 || t.length >= 300) return NodeFilter.FILTER_SKIP;
      allCandidates.push({text:t.substring(0,50), top:r.top.toFixed(0), bottom:r.bottom.toFixed(0), left:r.left.toFixed(0)});
      if (plrRect) {
        var bottomZone = plrRect.top + plrRect.height * 0.5;
        if (r.top < bottomZone || r.bottom > plrRect.bottom + 2) return NodeFilter.FILTER_SKIP;
        if (r.left < plrRect.left - 5 || r.right > plrRect.right + 5) return NodeFilter.FILTER_SKIP;
      }
      return NodeFilter.FILTER_ACCEPT;
    });
    var node;
    while (node = treeWalker.nextNode()) {
      matched.push((node.textContent || '').trim());
    }
    return { matched: matched, allCandidates: allCandidates };
  }

  function findSubtitleText() {
    var player = document.querySelector('bpx-player');
    var plrRect = getPlayerBounds();
    if (player && player.shadowRoot) {
      var result = scanTree(player.shadowRoot, plrRect);
      debugTick++;
      if (debugTick % 3 === 0 && (result.matched.length > 0 || result.allCandidates.length > 0)) {
        try {
          chrome.webview.postMessage('__gv_debug:shadow matched=' + JSON.stringify(result.matched.slice(0,3)) + ' candidates=' + JSON.stringify(result.allCandidates.slice(0,5)));
        } catch(e) {}
      }
      if (result.matched.length > 0) return result.matched[result.matched.length - 1];
    }
    // Fallback: scan document.body for subtitle-like elements
    var bodyResult = scanTree(document.body, plrRect);
    if (debugTick % 5 === 0 && bodyResult.matched.length > 0) {
      try {
        chrome.webview.postMessage('__gv_debug:body matched=' + JSON.stringify(bodyResult.matched.slice(0,3)));
      } catch(e) {}
    }
    if (bodyResult.matched.length > 0) return bodyResult.matched[bodyResult.matched.length - 1];
    return '';
  }

  function sendIfChanged(text) {
    if (text && text !== lastText) {
      lastText = text;
      try { chrome.webview.postMessage('__gv_cc:' + text); } catch(e) {}
      try { chrome.webview.postMessage('__gv_debug:cc_text=' + text); } catch(e) {}
    }
  }

  function observeShadow(shadow) {
    if (!shadow || shadow.__gv_observed) return;
    shadow.__gv_observed = true;
    var obs = new MutationObserver(function() {
      var t = findSubtitleText();
      sendIfChanged(t);
    });
    obs.observe(shadow, {childList: true, subtree: true, characterData: true});
  }

  // Also observe document body for non-shadow-DOM subtitle elements
  var bodyObs = new MutationObserver(function() {
    var t = findSubtitleText();
    sendIfChanged(t);
  });
  bodyObs.observe(document.body, {childList: true, subtree: true, characterData: true});

  setInterval(function() {
    var text = findSubtitleText();
    sendIfChanged(text);
    var player = document.querySelector('bpx-player');
    if (player && player.shadowRoot) {
      observeShadow(player.shadowRoot);
    }
  }, 500);
})();";
        _ = wv.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ccScript);

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
            if (wv == _webView)
                TitleChanged?.Invoke(tab.Title);
        };

        wv.CoreWebView2.NavigationStarting += (s, e) =>
        {
            tab.IsLoading = true;
            if (wv == _webView)
            {
                LoadingBar.Visibility = Visibility.Visible;
                LoadingBar.IsIndeterminate = true;
            }
            LoadingStateChanged?.Invoke(true);
        };

        wv.CoreWebView2.NavigationCompleted += (s, e) =>
        {
            tab.IsLoading = false;
            if (wv == _webView)
            {
                LoadingBar.Visibility = Visibility.Collapsed;
            }
            LoadingStateChanged?.Invoke(false);

            tab.CanGoBack = wv.CoreWebView2.CanGoBack;
            tab.CanGoForward = wv.CoreWebView2.CanGoForward;

            // Fire URL change only on real navigations (not pushState/hash changes)
            var sourceUrl = wv.Source?.ToString() ?? "";
            tab.Url = sourceUrl;
            if (wv == _webView)
                UrlChanged?.Invoke(sourceUrl);
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
        if (_webView == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        if (_webView.CoreWebView2 != null)
        {
            _webView.CoreWebView2.Navigate(url);
        }
        else
        {
            // CoreWebView2 not ready yet — set Source directly; it queues the navigation
            try
            {
                _webView.Source = uri;
            }
            catch (ObjectDisposedException)
            {
                // WebView2 was closed (e.g., tab closed); recreate for current active tab
                if (_tabManager?.ActiveTab != null)
                {
                    _tabManager.InvalidateWebViewCache(_tabManager.ActiveTab.Id);
                    LoadUrl(_tabManager.ActiveTab, url);
                }
            }
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

    public void RemoveWebView(string tabId)
    {
        var wv = _tabManager?.GetWebView(tabId);
        if (wv != null && WebViewHost.Children.Contains(wv))
        {
            WebViewHost.Children.Remove(wv);
        }
        if (_webView == wv)
            _webView = null;
    }
}
