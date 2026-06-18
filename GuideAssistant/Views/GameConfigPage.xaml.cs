using GuideAssistant.Data;
using GuideAssistant.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GuideAssistant.Views;

public sealed partial class GameConfigPage : Page
{
    private readonly GameRepository _repo;

    public GameConfigPage(GameRepository repo)
    {
        InitializeComponent();
        _repo = repo;
        LoadGames();
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

        var dialog = new ContentDialog
        {
            Title = "添加游戏",
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { nameBox, pathBox, helperBox, argsBox }
            },
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            _repo.Add(new GameConfig
            {
                GameName = nameBox.Text,
                GamePath = pathBox.Text,
                HelperPath = helperBox.Text,
                LaunchArgs = argsBox.Text
            });
            LoadGames();
        }
    }

    private async void EditBtn_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is not GameConfig config) return;

        var nameBox = new TextBox { Text = config.GameName, Margin = new Thickness(0, 0, 0, 8) };
        var pathBox = new TextBox { Text = config.GamePath, Margin = new Thickness(0, 0, 0, 8) };
        var helperBox = new TextBox { Text = config.HelperPath ?? "", Margin = new Thickness(0, 0, 0, 8) };
        var argsBox = new TextBox { Text = config.LaunchArgs ?? "" };

        var dialog = new ContentDialog
        {
            Title = "编辑游戏配置",
            Content = new StackPanel
            {
                Spacing = 4,
                Children = { nameBox, pathBox, helperBox, argsBox }
            },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            config.GameName = nameBox.Text;
            config.GamePath = pathBox.Text;
            config.HelperPath = helperBox.Text;
            config.LaunchArgs = argsBox.Text;
            _repo.Update(config);
            LoadGames();
        }
    }

    private void DeleteBtn_Click(object sender, RoutedEventArgs e)
    {
        if (GameList.SelectedItem is GameConfig config)
        {
            _repo.Delete(config.Id);
            LoadGames();
        }
    }
}
