using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuideAssistant.Models;

namespace GuideAssistant.Services;

public partial class TabManager : ObservableObject
{
    private readonly Dictionary<string, Microsoft.UI.Xaml.Controls.WebView2> _webViewCache = new();

    public ObservableCollection<TabItem> Tabs { get; } = new();

    [ObservableProperty]
    private TabItem? _activeTab;

    public TabManager()
    {
        // Create default tab
        AddTab("https://www.bilibili.com");
    }

    public TabItem AddTab(string? url = null)
    {
        var tab = new TabItem
        {
            Title = "新标签页",
            Url = url ?? "https://www.bilibili.com"
        };
        Tabs.Add(tab);
        ActiveTab = tab;
        return tab;
    }

    public void CloseTab(string tabId)
    {
        var tab = Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;

        if (_webViewCache.TryGetValue(tabId, out var wv))
        {
            wv.Close();
            _webViewCache.Remove(tabId);
        }

        Tabs.Remove(tab);

        if (Tabs.Count == 0)
            AddTab();
        else if (ActiveTab == null || ActiveTab.Id == tabId)
            ActiveTab = Tabs[^1];
    }

    public void CloseCurrentTab()
    {
        if (ActiveTab != null) CloseTab(ActiveTab.Id);
    }

    public Microsoft.UI.Xaml.Controls.WebView2 GetOrCreateWebView(TabItem tab, Func<TabItem, Microsoft.UI.Xaml.Controls.WebView2> factory)
    {
        if (!_webViewCache.ContainsKey(tab.Id))
        {
            var wv = factory(tab);
            _webViewCache[tab.Id] = wv;
        }
        return _webViewCache[tab.Id];
    }

    public void NavigateCurrentTab(string url)
    {
        if (ActiveTab == null) return;
        ActiveTab.Url = url;
        ActiveTab.BackHistory.Push(url);
    }

    public void Navigate(TabItem tab, string url)
    {
        tab.Url = url;
        if (_webViewCache.TryGetValue(tab.Id, out var wv))
        {
            wv.CoreWebView2?.Navigate(url);
        }
    }

    public string? GetBookmarkUrl()
    {
        return ActiveTab?.Url;
    }

    public string? GetBookmarkTitle()
    {
        return ActiveTab?.Title;
    }

    public void Cleanup()
    {
        foreach (var wv in _webViewCache.Values)
        {
            try { wv.Close(); } catch { }
        }
        _webViewCache.Clear();
    }
}
