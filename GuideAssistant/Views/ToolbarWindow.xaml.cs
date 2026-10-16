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
        _tabManager.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(TabManager.ActiveTab))
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

    private async void SaveVideoMomentBtn_Click(object sender, RoutedEventArgs e)
    {
        SaveMomentButton.IsEnabled = false;
        try
        {
            MomentStatusText.Text = await _viewModel.SaveVideoMomentAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Video moment save failed");
            MomentStatusText.Text = "保存失败，请检查日志";
        }
        finally
        {
            SaveMomentButton.IsEnabled = true;
        }
    }

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
            else if (e.PropertyName == nameof(ToolbarViewModel.IsCurrentBookmarked))
                NavBarControl.SetBookmarkState(_viewModel.IsCurrentBookmarked);
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
        else if (BookmarkGrouping.SelectedIndex == 0)
        {
            var gameNodes = new Dictionary<int, BookmarkTreeNode>();
            foreach (var game in _bookmarkService.GetAllGames())
            {
                var node = new BookmarkTreeNode { IconGlyph = "\uE7FC", Label = game.GameName, IsFolder = true };
                gameNodes[game.Id] = node;
                _bookmarkRootNodes.Add(node);
            }

            var general = new BookmarkTreeNode { IconGlyph = "\uE8B7", Label = "通用收藏", IsFolder = true };
            var legacyNodes = new Dictionary<int, BookmarkTreeNode>();
            foreach (var bm in allBookmarks)
            {
                BookmarkTreeNode target;
                if (bm.GameConfigId is int gameId && gameNodes.TryGetValue(gameId, out var gameNode))
                    target = gameNode;
                else if (bm.GameId is int legacyId && !string.IsNullOrEmpty(bm.GameName))
                {
                    if (!legacyNodes.TryGetValue(legacyId, out var legacyNode))
                    {
                        legacyNode = new BookmarkTreeNode { IconGlyph = "\uE8B7", Label = bm.GameName + "（历史游戏）", IsFolder = true };
                        legacyNodes[legacyId] = legacyNode;
                        _bookmarkRootNodes.Add(legacyNode);
                    }
                    target = legacyNode;
                }
                else target = general;
                target.Children.Add(CreateBookmarkNode(bm));
            }
            _bookmarkRootNodes.Add(general);
            foreach (var node in _bookmarkRootNodes)
                node.Label = $"{node.Label}  ({node.Children.Count})";
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
            SubLabel = string.Join(" · ", new[] { bm.GameName, bm.Tags, bm.Url }
                .Where(value => !string.IsNullOrWhiteSpace(value))),
            Bookmark = bm
        };
    }

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _viewModel.SearchBookmarksCommand.Execute(args.QueryText);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_viewModel != null && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            _viewModel.SearchBookmarksCommand.Execute(sender.Text);
    }

    private void BookmarkGrouping_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel != null) RefreshBookmarksPanel();
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

        if (node.IsFolder && node.FolderId > 0)
            ShowFolderMenu(node, (FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
        else if (node.Bookmark != null)
            ShowBookmarkMenu(node, (FrameworkElement)sender, e.GetPosition((FrameworkElement)sender));
    }

    private void ShowFolderMenu(BookmarkTreeNode folderNode, FrameworkElement target, Windows.Foundation.Point offset)
    {
        var folder = _bookmarkService.GetAllFolders().FirstOrDefault(f => f.Id == folderNode.FolderId);
        if (folder == null) return;
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
                Content = new TextBox { Text = folder.Name },
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
        var editItem = new MenuFlyoutItem { Text = "编辑收藏" };
        editItem.Click += async (s, args) => await EditBookmarkAsync(bookmarkNode.Bookmark!);
        menu.Items.Add(editItem);

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

    private sealed record BookmarkChoice(int? Id, string Name);

    private async Task EditBookmarkAsync(Bookmark bookmark)
    {
        var titleBox = new TextBox { Header = "标题", Text = bookmark.Title };
        var tagsBox = new TextBox { Header = "标签", PlaceholderText = "例如：Boss、收集、支线", Text = bookmark.Tags ?? "" };
        var notesBox = new TextBox { Header = "备注", Text = bookmark.Notes ?? "", AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 120 };
        var games = new List<BookmarkChoice> { new(null, "通用收藏") };
        games.AddRange(_bookmarkService.GetAllGames().Select(g => new BookmarkChoice(g.Id, g.GameName)));
        // Keep unmapped historical associations until the user explicitly changes them.
        if (bookmark.GameId != null && bookmark.GameConfigId == null)
            games.Add(new(-1, bookmark.GameName ?? "历史游戏"));
        var gameBox = new ComboBox { Header = "所属游戏", ItemsSource = games, DisplayMemberPath = "Name",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedItem = games.FirstOrDefault(g => g.Id == (bookmark.GameConfigId ?? (bookmark.GameId != null ? -1 : null))) ?? games[0] };
        var folders = new List<BookmarkChoice> { new(null, "未分类") };
        folders.AddRange(_bookmarkService.GetAllFolders().Select(f => new BookmarkChoice(f.Id, f.Name)));
        var folderBox = new ComboBox { Header = "文件夹", ItemsSource = folders, DisplayMemberPath = "Name",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            SelectedItem = folders.FirstOrDefault(f => f.Id == bookmark.FolderId) ?? folders[0] };
        var errorText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var dialog = new ContentDialog
        {
            Title = "编辑收藏", PrimaryButtonText = "保存", CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot,
            Content = new ScrollViewer
            {
                MaxHeight = 420,
                Content = new StackPanel { Spacing = 10, Children = { titleBox, gameBox, folderBox, tagsBox, notesBox, errorText } }
            }
        };
        dialog.PrimaryButtonClick += (s, args) =>
        {
            if (string.IsNullOrWhiteSpace(titleBox.Text))
            {
                errorText.Text = "请输入收藏标题。";
                args.Cancel = true;
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var selectedGameId = (gameBox.SelectedItem as BookmarkChoice)?.Id;
        bookmark.Title = titleBox.Text.Trim();
        bookmark.Tags = tagsBox.Text.Trim();
        bookmark.Notes = notesBox.Text.Trim();
        if (selectedGameId != -1)
        {
            bookmark.GameId = null;
            bookmark.GameConfigId = selectedGameId;
        }
        bookmark.FolderId = (folderBox.SelectedItem as BookmarkChoice)?.Id;
        try
        {
            _bookmarkService.Update(bookmark);
            MomentStatusText.Text = "收藏已保存";
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Bookmark edit failed");
            MomentStatusText.Text = "收藏保存失败，请重试";
            RefreshBookmarksPanel();
        }
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
