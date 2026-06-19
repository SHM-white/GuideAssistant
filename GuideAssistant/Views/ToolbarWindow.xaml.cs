using GuideAssistant.Models;
using GuideAssistant.Services;
using GuideAssistant.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace GuideAssistant.Views;

public sealed partial class ToolbarWindow : Window
{
    private readonly ToolbarViewModel _viewModel;
    private readonly TabManager _tabManager;
    private readonly BookmarkService _bookmarkService;
    private readonly GameDetector _gameDetector;
    private bool _suppressSelectionChanged;

    private readonly Dictionary<TreeViewNode, Bookmark> _bookmarkNodeMap = new();

    public ToolbarWindow(
        ToolbarViewModel viewModel,
        TabManager tabManager,
        BookmarkService bookmarkService,
        GameDetector gameDetector)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _tabManager = tabManager;
        _bookmarkService = bookmarkService;
        _gameDetector = gameDetector;

        InitializeWindow();
        InitializeTabList();
        InitializeNavBar();
        InitializeBookmarksPanel();
        WireViewModelEvents();
    }

    private void InitializeWindow()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 800, Height = 320 });
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null) presenter.IsAlwaysOnTop = true;
        Closed += (s, e) => Log.Information("ToolbarWindow closed");
        Log.Information("ToolbarWindow initialized");
    }

    private void WireViewModelEvents()
    {
        _viewModel.NavigateToUrlRequested += url =>
        {
            NavBarControl.SetUrl(url);
            // Forward to MainViewModel via its NavigateToUrlCommand (accessed via DI)
        };

        _viewModel.BookmarkChanged += RefreshBookmarksPanel;
        _viewModel.BookmarksRefreshRequested += RefreshBookmarksPanel;

        _bookmarkService.BookmarksChanged += () =>
            DispatcherQueue.TryEnqueue(RefreshBookmarksPanel);
    }

    // ── Tab List ─────────────────────────────────────────

    private void InitializeTabList()
    {
        _tabManager.Tabs.CollectionChanged += (s, e) =>
        {
            TabListView.ItemsSource = null;
            TabListView.ItemsSource = _tabManager.Tabs;
            SelectActiveTab();
        };
        TabListView.ItemsSource = _tabManager.Tabs;
        SelectActiveTab();
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
            _viewModel.SelectTabCommand.Execute(tab);
    }

    private void TabFavoriteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TabItem tab)
            _viewModel.QuickBookmarkTabCommand.Execute(tab);
    }

    private void TabCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TabItem tab)
            _viewModel.CloseTabItemCommand.Execute(tab);
    }

    private void NewTabBtn_Click(object sender, RoutedEventArgs e)
        => _viewModel.AddNewTabCommand.Execute(null);

    // ── Navigation Bar ───────────────────────────────────

    private void InitializeNavBar()
    {
        NavBarControl.NavigateRequested += url => _viewModel.NavigateCommand.Execute(url);
        NavBarControl.BackRequested += () => _viewModel.NavigateBackCommand.Execute(null);
        NavBarControl.ForwardRequested += () => _viewModel.NavigateForwardCommand.Execute(null);
        NavBarControl.RefreshRequested += () => _viewModel.NavigateRefreshCommand.Execute(null);
        NavBarControl.BookmarkRequested += () => _viewModel.BookmarkCurrentPageCommand.Execute(null);
        NavBarControl.SettingsRequested += () => _viewModel.OpenSettingsCommand.Execute(null);

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ToolbarViewModel.UrlBarText))
            {
                NavBarControl.SetUrl(_viewModel.UrlBarText);
                NavBarControl.SetBookmarkState(_viewModel.IsCurrentBookmarked);
            }
        };

        if (_tabManager.ActiveTab != null)
        {
            NavBarControl.SetUrl(_tabManager.ActiveTab.Url);
            NavBarControl.SetBookmarkState(_bookmarkService.IsUrlBookmarked(_tabManager.ActiveTab.Url));
        }
    }

    // ── Bookmarks Panel ──────────────────────────────────

    private void InitializeBookmarksPanel() => RefreshBookmarksPanel();

    private void RefreshBookmarksPanel()
    {
        BookmarksTreeView.RootNodes.Clear();
        _bookmarkNodeMap.Clear();

        var allBookmarks = string.IsNullOrWhiteSpace(_viewModel.BookmarkSearchText)
            ? _bookmarkService.GetAll()
            : _bookmarkService.Search(_viewModel.BookmarkSearchText);

        var allGames = _bookmarkService.GetAllGames();
        var gameLookup = allGames.ToDictionary(g => g.Id, g => g.GameName);

        if (!string.IsNullOrWhiteSpace(_viewModel.BookmarkSearchText))
        {
            var folderNode = new TreeViewNode { Content = $"🔍 搜索结果 ({allBookmarks.Count})", IsExpanded = true };
            foreach (var bm in allBookmarks)
            {
                var node = new TreeViewNode { Content = $"📺 {bm.Title}\n   {bm.Url}" };
                _bookmarkNodeMap[node] = bm;
                folderNode.Children.Add(node);
            }
            BookmarksTreeView.RootNodes.Add(folderNode);
            return;
        }

        var grouped = allBookmarks
            .GroupBy(b => b.GameId.HasValue && gameLookup.ContainsKey(b.GameId.Value) ? gameLookup[b.GameId.Value] : "未分类")
            .OrderBy(g => g.Key == "未分类" ? 1 : 0).ThenBy(g => g.Key);

        foreach (var group in grouped)
        {
            var folderNode = new TreeViewNode { Content = $"📁 {group.Key}  ({group.Count()})", IsExpanded = true };
            foreach (var bm in group)
            {
                var node = new TreeViewNode { Content = $"📺 {bm.Title}\n   {bm.Url}" };
                _bookmarkNodeMap[node] = bm;
                folderNode.Children.Add(node);
            }
            BookmarksTreeView.RootNodes.Add(folderNode);
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _viewModel.SearchBookmarksCommand.Execute(args.QueryText);
        RefreshBookmarksPanel();
    }

    private void BookmarksTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is TreeViewNode node && _bookmarkNodeMap.TryGetValue(node, out var bookmark))
        {
            var tab = _tabManager.AddTab(bookmark.Url);
            _viewModel.SelectTabCommand.Execute(tab);
            NavBarControl.SetUrl(bookmark.Url);
        }
    }
}
