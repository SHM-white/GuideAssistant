using GuideAssistant.Models;
using GuideAssistant.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace GuideAssistant.Views;

public sealed partial class ToolbarWindow : Window
{
    private MainWindow _mainWindow = default!;
    private readonly TabManager _tabManager;
    private readonly BookmarkService _bookmarkService;
    private readonly GameDetector _gameDetector;
    private bool _suppressSelectionChanged;

    public ToolbarWindow(TabManager tabManager, BookmarkService bookmarkService, GameDetector gameDetector)
    {
        InitializeComponent();
        _tabManager = tabManager;
        _bookmarkService = bookmarkService;
        _gameDetector = gameDetector;

        InitializeWindow();
    }

    /// <summary>
    /// Sets the MainWindow reference after both windows have been constructed.
    /// This breaks the DI circular dependency: MainWindow → ToolbarWindow → MainWindow.
    /// </summary>
    public void SetMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
        InitializeTabList();
        InitializeNavBar();
        SubscribeToMainWindowEvents();

        // Auto-load the active tab's page on startup
        if (_tabManager.ActiveTab != null)
            _mainWindow.NavigateToUrl(_tabManager.ActiveTab.Url);

        InitializeBookmarksPanel();
    }

    private void InitializeWindow()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 800, Height = 320 });
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
            presenter.IsAlwaysOnTop = true;

        Closed += (s, e) => Log.Information("ToolbarWindow closed");
        Log.Information("ToolbarWindow initialized");
    }

    private void InitializeTabList()
    {
        _tabManager.Tabs.CollectionChanged += (s, e) => RefreshTabList();
        RefreshTabList();
        SelectActiveTab();
    }

    private void RefreshTabList()
    {
        TabListView.ItemsSource = null;
        TabListView.ItemsSource = _tabManager.Tabs;
    }

    private void SelectActiveTab()
    {
        if (_tabManager.ActiveTab != null)
        {
            _suppressSelectionChanged = true;
            TabListView.SelectedItem = _tabManager.ActiveTab;
            _suppressSelectionChanged = false;
        }
    }

    private void TabListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelectionChanged) return;

        if (TabListView.SelectedItem is TabItem tab)
        {
            _mainWindow.SwitchToTabById(tab.Id);
            NavBarControl.SetUrl(tab.Url);
        }
    }

    private void TabFavoriteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TabItem tab)
        {
            if (_bookmarkService.IsUrlBookmarked(tab.Url))
            {
                ShowDialog("提示", "已收藏过该页面");
            }
            else
            {
                int? gameId = ResolveForegroundGameId();
                _bookmarkService.QuickAdd(tab.Title, tab.Url, gameId: gameId);
                ShowDialog("收藏", "已添加到收藏夹 ✓");
            }
            RefreshTabList();
            SelectActiveTab();
        }
    }

    private int? ResolveForegroundGameId()
    {
        var gameName = _gameDetector.CurrentForegroundGameName;
        if (string.IsNullOrEmpty(gameName)) return null;
        var games = _bookmarkService.GetAllGames();
        return games.FirstOrDefault(g =>
            string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private void TabCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TabItem tab)
        {
            _tabManager.CloseTab(tab.Id);
            _mainWindow.RemoveWebViewForTab(tab.Id);
            RefreshTabList();
            SelectActiveTab();

            if (_tabManager.ActiveTab != null)
            {
                _mainWindow.SwitchToTabById(_tabManager.ActiveTab.Id);
                NavBarControl.SetUrl(_tabManager.ActiveTab.Url);
            }
        }
    }

    private void NewTabBtn_Click(object sender, RoutedEventArgs e)
    {
        var tab = _tabManager.AddTab();
        RefreshTabList();
        SelectActiveTab();
        // Switch to the new tab (creates its WebView) without touching other tabs
        _mainWindow.SwitchToTabById(tab.Id);
        NavBarControl.SetUrl(tab.Url);
    }

    private void InitializeNavBar()
    {
        NavBarControl.NavigateRequested += (url) =>
        {
            _mainWindow.NavigateToUrl(url);
            NavBarControl.SetUrl(url);
        };

        NavBarControl.BackRequested += () =>
        {
            _mainWindow.NavigateBack();
            var currentUrl = _mainWindow.GetCurrentUrl();
            if (currentUrl != null) NavBarControl.SetUrl(currentUrl);
        };

        NavBarControl.ForwardRequested += () =>
        {
            _mainWindow.NavigateForward();
            var currentUrl = _mainWindow.GetCurrentUrl();
            if (currentUrl != null) NavBarControl.SetUrl(currentUrl);
        };

        NavBarControl.RefreshRequested += () => _mainWindow.NavigateRefresh();

        NavBarControl.BookmarkRequested += () =>
        {
            var url = _mainWindow.GetCurrentUrl();
            if (string.IsNullOrEmpty(url)) return;

            if (_bookmarkService.IsUrlBookmarked(url))
            {
                ShowDialog("提示", "已收藏过该页面");
            }
            else
            {
                _mainWindow.AddBookmark();
                NavBarControl.SetBookmarkState(true);
                ShowDialog("收藏", "已添加到收藏夹 ✓");
            }
        };

        NavBarControl.SettingsRequested += () => _mainWindow.ShowSettingsPage();

        // Set initial URL
        var initialUrl = _mainWindow.GetCurrentUrl();
        if (initialUrl != null)
        {
            NavBarControl.SetUrl(initialUrl);
        }
    }

    private void SubscribeToMainWindowEvents()
    {
        _mainWindow.UrlChanged += (url) =>
        {
            NavBarControl.SetUrl(url);
            RefreshTabList();
            SelectActiveTab();
        };

        _mainWindow.TitleChanged += (title) =>
        {
            RefreshTabList();
        };
    }

    private void InitializeBookmarksPanel()
    {
        _bookmarkService.BookmarksChanged += () =>
            DispatcherQueue.TryEnqueue(RefreshBookmarksPanel);

        RefreshBookmarksPanel();
    }

    private readonly Dictionary<TreeViewNode, Bookmark> _bookmarkNodeMap = new();

    private void RefreshBookmarksPanel()
    {
        BookmarksTreeView.RootNodes.Clear();
        _bookmarkNodeMap.Clear();

        var allBookmarks = _bookmarkService.GetAll();
        var allGames = _bookmarkService.GetAllGames();
        var gameLookup = allGames.ToDictionary(g => g.Id, g => g.GameName);

        var grouped = allBookmarks
            .GroupBy(b => b.GameId.HasValue && gameLookup.ContainsKey(b.GameId.Value)
                ? gameLookup[b.GameId.Value]
                : "未分类")
            .OrderBy(g => g.Key == "未分类" ? 1 : 0)
            .ThenBy(g => g.Key);

        foreach (var group in grouped)
        {
            var folderNode = new TreeViewNode
            {
                Content = $"📁 {group.Key}  ({group.Count()})",
                IsExpanded = true
            };

            foreach (var bm in group)
            {
                var node = new TreeViewNode
                {
                    Content = $"📺 {bm.Title}\n   {bm.Url}"
                };
                _bookmarkNodeMap[node] = bm;
                folderNode.Children.Add(node);
            }

            BookmarksTreeView.RootNodes.Add(folderNode);
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.QueryText))
        {
            RefreshBookmarksPanel();
            return;
        }

        var results = _bookmarkService.Search(args.QueryText);
        BookmarksTreeView.RootNodes.Clear();
        _bookmarkNodeMap.Clear();

        if (results.Count > 0)
        {
            var folderNode = new TreeViewNode
            {
                Content = $"🔍 搜索结果 ({results.Count})",
                IsExpanded = true
            };

            foreach (var bm in results)
            {
                var node = new TreeViewNode
                {
                    Content = $"📺 {bm.Title}\n   {bm.Url}"
                };
                _bookmarkNodeMap[node] = bm;
                folderNode.Children.Add(node);
            }

            BookmarksTreeView.RootNodes.Add(folderNode);
        }
        else
        {
            var node = new TreeViewNode { Content = "未找到匹配的收藏项" };
            BookmarksTreeView.RootNodes.Add(node);
        }
    }

    private void BookmarksTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node && _bookmarkNodeMap.TryGetValue(node, out var bookmark))
        {
            var tab = _tabManager.AddTab(bookmark.Url);
            _mainWindow.SwitchToTabById(tab.Id);
            NavBarControl.SetUrl(bookmark.Url);
            RefreshTabList();
            SelectActiveTab();
        }
    }

    private async void ShowDialog(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "确定",
            XamlRoot = Content.XamlRoot
        };
        await dialog.ShowAsync();
    }
}
