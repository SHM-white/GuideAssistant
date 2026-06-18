using GuideAssistant.Models;
using GuideAssistant.Services;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Serilog;

namespace GuideAssistant.Views;

public sealed partial class ToolbarWindow : Window
{
    private readonly MainWindow _mainWindow;
    private readonly TabManager _tabManager;
    private readonly BookmarkService _bookmarkService;
    private bool _isBookmarked;

    public ToolbarWindow(MainWindow mainWindow, TabManager tabManager, BookmarkService bookmarkService)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        _tabManager = tabManager;
        _bookmarkService = bookmarkService;

        InitializeWindow();
        InitializeTabBar();
        InitializeNavBar();
        SubscribeToMainWindowEvents();
    }

    private void InitializeWindow()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);

        AppWindow.Resize(new Windows.Graphics.SizeInt32 { Width = 800, Height = 220 });
        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
            presenter.IsAlwaysOnTop = true;

        Closed += (s, e) => Log.Information("ToolbarWindow closed");
        Log.Information("ToolbarWindow initialized");
    }

    private void InitializeTabBar()
    {
        TabBarControl.TabManager = _tabManager;

        TabBarControl.NewTabRequested += () =>
        {
            var tab = _tabManager.AddTab();
            TabBarControl.Refresh();
            _mainWindow.NavigateToUrl(tab.Url);
            LoadUrlIntoNavBar(tab.Url);
        };

        TabBarControl.TabCloseRequested += (id) =>
        {
            _tabManager.CloseTab(id);
            TabBarControl.Refresh();
        };

        TabBarControl.TabSelected += (id) =>
        {
            var tab = _tabManager.Tabs.FirstOrDefault(t => t.Id == id);
            if (tab != null)
            {
                _tabManager.ActiveTab = tab;
                _mainWindow.NavigateToUrl(tab.Url);
                LoadUrlIntoNavBar(tab.Url);
                UpdateBookmarkState();
            }
        };

        _tabManager.Tabs.CollectionChanged += (s, e) => TabBarControl.Refresh();

        // Initial refresh
        TabBarControl.Refresh();
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
            // Update URL bar after navigation
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
                _isBookmarked = true;
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
            UpdateBookmarkState();
        }
    }

    private void SubscribeToMainWindowEvents()
    {
        _mainWindow.UrlChanged += (url) =>
        {
            NavBarControl.SetUrl(url);
            UpdateBookmarkState();
        };

        _mainWindow.TitleChanged += (title) =>
        {
            // TabBar refreshes via TabManager collection change
        };

        _mainWindow.LoadingStateChanged += (isLoading) =>
        {
            // Could update a loading indicator if desired
        };
    }

    private void LoadUrlIntoNavBar(string url)
    {
        NavBarControl.SetUrl(url);
        UpdateBookmarkState();
    }

    private void UpdateBookmarkState()
    {
        _isBookmarked = _mainWindow.IsCurrentUrlBookmarked();
        NavBarControl.SetBookmarkState(_isBookmarked);
    }

    private void CloseTabBtn_Click(object sender, RoutedEventArgs e)
    {
        _tabManager.CloseCurrentTab();
        TabBarControl.Refresh();
    }

    private void BookmarkBtn_Click(object sender, RoutedEventArgs e)
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
            _isBookmarked = true;
            NavBarControl.SetBookmarkState(true);
            ShowDialog("收藏", "已添加到收藏夹 ✓");
        }
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.QueryText)) return;

        var results = _bookmarkService.Search(args.QueryText);
        if (results.Count > 0)
        {
            BookmarkResultsList.ItemsSource = results;
            BookmarkResultsList.Visibility = Visibility.Visible;
        }
        else
        {
            ShowDialog("搜索", "未找到匹配的收藏项");
        }
    }

    private void BookmarkResults_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (BookmarkResultsList.SelectedItem is Bookmark bookmark)
        {
            _mainWindow.NavigateToUrl(bookmark.Url);
            NavBarControl.SetUrl(bookmark.Url);
            BookmarkResultsList.Visibility = Visibility.Collapsed;
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
