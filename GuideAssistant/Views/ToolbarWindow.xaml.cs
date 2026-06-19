using GuideAssistant.Models;
using GuideAssistant.Services;
using GuideAssistant.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Serilog;
using System.Collections.ObjectModel;

namespace GuideAssistant.Views;

public sealed partial class ToolbarWindow : Window
{
    private readonly ToolbarViewModel _viewModel;
    private readonly TabManager _tabManager;
    private readonly BookmarkService _bookmarkService;
    private readonly GameDetector _gameDetector;
    private bool _suppressSelectionChanged;
    private bool _allowClose;

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    private ObservableCollection<BookmarkTreeNode>? _bookmarkRootNodes;

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

        AppWindow.Closing += (sender, args) =>
        {
            if (_allowClose) return;
            args.Cancel = true;
            AppWindow.Hide();
            Log.Information("ToolbarWindow hidden to tray");
        };

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
        var allBookmarks = string.IsNullOrWhiteSpace(_viewModel.BookmarkSearchText)
            ? _bookmarkService.GetAll()
            : _bookmarkService.Search(_viewModel.BookmarkSearchText);

        var folders = _bookmarkService.GetAllFolders();
        _bookmarkRootNodes = new ObservableCollection<BookmarkTreeNode>();

        if (!string.IsNullOrWhiteSpace(_viewModel.BookmarkSearchText))
        {
            var searchFolder = new BookmarkTreeNode
            {
                IconGlyph = "\uE721",
                Label = $"搜索结果 ({allBookmarks.Count})",
                IsFolder = true
            };
            foreach (var bm in allBookmarks)
            {
                searchFolder.Children.Add(CreateBookmarkNode(bm));
            }
            _bookmarkRootNodes.Add(searchFolder);
        }
        else
        {
            // Build folder nodes (including empty folders)
            var folderNodes = new Dictionary<int, BookmarkTreeNode>();
            foreach (var f in folders)
            {
                var node = new BookmarkTreeNode
                {
                    IconGlyph = "\uE8B7",
                    Label = f.Name,
                    IsFolder = true,
                    FolderId = f.Id
                };
                folderNodes[f.Id] = node;
                _bookmarkRootNodes.Add(node);
            }

            // Add "未分类" folder
            var uncategorizedNode = new BookmarkTreeNode
            {
                IconGlyph = "\uE8B7",
                Label = "未分类",
                IsFolder = true,
                FolderId = 0
            };
            _bookmarkRootNodes.Add(uncategorizedNode);

            // Distribute bookmarks into folders
            foreach (var bm in allBookmarks)
            {
                var targetNode = bm.FolderId.HasValue && folderNodes.TryGetValue(bm.FolderId.Value, out var fn)
                    ? fn : uncategorizedNode;
                targetNode.Children.Add(CreateBookmarkNode(bm));
            }

            // Update labels with counts
            foreach (var node in _bookmarkRootNodes)
            {
                node.Label = $"{node.Label}  ({node.Children.Count})";
            }
        }

        BookmarksTreeView.ItemsSource = _bookmarkRootNodes;
    }

    private static BookmarkTreeNode CreateBookmarkNode(Bookmark bm)
    {
        return new BookmarkTreeNode
        {
            IconGlyph = "\uE7F4",
            Label = bm.Title,
            SubLabel = bm.Url,
            Bookmark = bm
        };
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _viewModel.SearchBookmarksCommand.Execute(args.QueryText);
        RefreshBookmarksPanel();
    }

    private void BookmarksTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is BookmarkTreeNode { Bookmark: not null } node)
        {
            var bookmark = node.Bookmark;
            var tab = _tabManager.AddTab(bookmark.Url);
            _viewModel.SelectTabCommand.Execute(tab);
            NavBarControl.SetUrl(bookmark.Url);
        }
    }

    // ── Folder & Context Menu ──────────────────────────

    private async void NewFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "新建文件夹",
            PrimaryButtonText = "创建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            Content = new TextBox { PlaceholderText = "输入文件夹名称" },
            XamlRoot = Content.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var name = ((TextBox)dialog.Content).Text.Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                _bookmarkService.AddFolder(name);
                RefreshBookmarksPanel();
            }
        }
    }

    private void BookmarksTreeView_RightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
    {
        var node = FindTreeViewNode(e.OriginalSource as DependencyObject);
        if (node == null) return;

        if (node.IsFolder)
            ShowFolderMenu(node, (FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
        else if (node.Bookmark != null)
            ShowBookmarkMenu(node, (FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
    }

    private void ShowFolderMenu(BookmarkTreeNode folderNode, FrameworkElement target, Windows.Foundation.Point offset)
    {
        var menu = new MenuFlyout();

        var renameItem = new MenuFlyoutItem { Text = "重命名" };
        renameItem.Click += async (s, args) =>
        {
            var dialog = new ContentDialog
            {
                Title = "重命名文件夹",
                PrimaryButtonText = "确定",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                Content = new TextBox { Text = folderNode.Label },
                XamlRoot = Content.XamlRoot
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var name = ((TextBox)dialog.Content).Text.Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    _bookmarkService.RenameFolder(folderNode.FolderId, name);
                    RefreshBookmarksPanel();
                }
            }
        };
        menu.Items.Add(renameItem);

        var deleteItem = new MenuFlyoutItem { Text = "删除文件夹" };
        deleteItem.Click += (s, args) =>
        {
            _bookmarkService.DeleteFolder(folderNode.FolderId);
            RefreshBookmarksPanel();
        };
        menu.Items.Add(deleteItem);

        menu.ShowAt(target, offset);
    }

    private void ShowBookmarkMenu(BookmarkTreeNode bookmarkNode, FrameworkElement target, Windows.Foundation.Point offset)
    {
        var menu = new MenuFlyout();

        // Move to folder submenu
        var moveSubMenu = new MenuFlyoutSubItem { Text = "移动到..." };
        var folders = _bookmarkService.GetAllFolders();

        var uncategorizedItem = new MenuFlyoutItem { Text = "未分类" };
        uncategorizedItem.Click += (s, args) =>
        {
            _bookmarkService.MoveBookmarkToFolder(bookmarkNode.Bookmark!.Id, null);
            RefreshBookmarksPanel();
        };
        moveSubMenu.Items.Add(uncategorizedItem);

        if (folders.Count > 0)
        {
            moveSubMenu.Items.Add(new MenuFlyoutSeparator());
            foreach (var f in folders)
            {
                var item = new MenuFlyoutItem { Text = f.Name };
                var folderId = f.Id;
                item.Click += (s, args) =>
                {
                    _bookmarkService.MoveBookmarkToFolder(bookmarkNode.Bookmark!.Id, folderId);
                    RefreshBookmarksPanel();
                };
                moveSubMenu.Items.Add(item);
            }
        }
        menu.Items.Add(moveSubMenu);

        menu.Items.Add(new MenuFlyoutSeparator());

        var deleteItem = new MenuFlyoutItem { Text = "删除收藏" };
        deleteItem.Click += (s, args) =>
        {
            _bookmarkService.Delete(bookmarkNode.Bookmark!.Id);
            RefreshBookmarksPanel();
        };
        menu.Items.Add(deleteItem);

        menu.ShowAt(target, offset);
    }

    private static BookmarkTreeNode? FindTreeViewNode(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is TreeViewItem tvi && tvi.DataContext is BookmarkTreeNode node)
                return node;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }
}
