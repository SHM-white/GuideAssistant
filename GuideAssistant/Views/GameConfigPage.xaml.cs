using GuideAssistant.Data;
using GuideAssistant.Models;
using GuideAssistant.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GuideAssistant.Views;

public sealed partial class GameConfigPage : Page
{
    private readonly GameRepository _repo;
    private readonly GameDetector _detector;
    private readonly HotkeyConfigManager _hotkeys;
    public event Action? GamesChanged;

    public GameConfigPage(GameRepository repo, GameDetector detector, HotkeyConfigManager hotkeys)
    {
        InitializeComponent();
        _repo = repo;
        _detector = detector;
        _hotkeys = hotkeys;
        LoadGames();
    }

    private void NotifyGameConfigurationsChanged()
    {
        LoadGames();
        _detector.ReloadConfigs();
        GamesChanged?.Invoke();
    }

    private async Task ShowInvalidGameAsync()
    {
        await new ContentDialog
        {
            Title = "游戏配置不完整",
            Content = "请填写游戏名称和游戏可执行文件（.exe）的完整路径。",
            CloseButtonText = "知道了",
            XamlRoot = this.XamlRoot
        }.ShowAsync();
    }

    private void LoadGames()
    {
        GameList.ItemsSource = _repo.GetAll();
    }

    private async void AddBtn_Click(object sender, RoutedEventArgs e)
    {
        var nameBox = new TextBox { PlaceholderText = "游戏名称", Margin = new Thickness(0, 0, 0, 8) };
        var pathBox = new TextBox { PlaceholderText = "游戏 exe 路径", Margin = new Thickness(0, 0, 0, 8) };
        var helperBox = new TextBox { PlaceholderText = "辅助器 exe 路径 (可选)", Margin = new Thickness(0, 0, 0, 8) };
        var argsBox = new TextBox { PlaceholderText = "启动参数 (可选)" };
        var autoDetect = new CheckBox { Content = "检测到游戏时自动应用专属快捷键", IsChecked = true };

        var dialog = new ContentDialog
        {
            Title = "添加游戏",
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { nameBox, pathBox, helperBox, argsBox, autoDetect }
            },
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text) ||
                !pathBox.Text.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                await ShowInvalidGameAsync();
                return;
            }
            _repo.Add(new GameConfig
            {
                GameName = nameBox.Text.Trim(),
                GamePath = pathBox.Text.Trim(),
                HelperPath = helperBox.Text.Trim(),
                LaunchArgs = argsBox.Text.Trim(),
                AutoDetect = autoDetect.IsChecked == true
            });
            NotifyGameConfigurationsChanged();
        }
    }

    private async void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not GameConfig config) return;

        var nameBox = new TextBox { Text = config.GameName, Margin = new Thickness(0, 0, 0, 8) };
        var pathBox = new TextBox { Text = config.GamePath, Margin = new Thickness(0, 0, 0, 8) };
        var helperBox = new TextBox { Text = config.HelperPath ?? "", Margin = new Thickness(0, 0, 0, 8) };
        var argsBox = new TextBox { Text = config.LaunchArgs ?? "" };
        var autoDetect = new CheckBox { Content = "检测到游戏时自动应用专属快捷键", IsChecked = config.AutoDetect };

        var dialog = new ContentDialog
        {
            Title = "编辑游戏配置",
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { nameBox, pathBox, helperBox, argsBox, autoDetect }
            },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text) ||
                !pathBox.Text.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                await ShowInvalidGameAsync();
                return;
            }
            config.GameName = nameBox.Text.Trim();
            config.GamePath = pathBox.Text.Trim();
            config.HelperPath = helperBox.Text.Trim();
            config.LaunchArgs = argsBox.Text.Trim();
            config.AutoDetect = autoDetect.IsChecked == true;
            _repo.Update(config);
            NotifyGameConfigurationsChanged();
        }
    }

    private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not GameConfig config) return;
        var dialog = new ContentDialog
        {
            Title = "删除游戏配置",
            Content = $"删除“{config.GameName}”及其专属快捷键覆盖？全局方案不会受到影响。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        _repo.Delete(config.Id);
        _hotkeys.ResetGameProfile(config.Id);
        NotifyGameConfigurationsChanged();
    }
}
