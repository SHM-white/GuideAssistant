using GuideAssistant.Models;
using GuideAssistant.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GuideAssistant.Controls;

public sealed partial class TabBar : UserControl
{
    public TabManager? TabManager { get; set; }

    public event Action<string>? TabSelected;
    public event Action? NewTabRequested;
    public event Action<string>? TabCloseRequested;

    public TabBar()
    {
        InitializeComponent();
    }

    public void Refresh()
    {
        if (TabManager == null) return;
        TabsItemsControl.ItemsSource = null;
        TabsItemsControl.ItemsSource = TabManager.Tabs;
    }

    private void TabItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border border && border.DataContext is TabItem tab)
        {
            TabSelected?.Invoke(tab.Id);
            // Handle close button tap
            if (e.OriginalSource is Button btn && btn.Content?.ToString() == "✕")
            {
                TabCloseRequested?.Invoke(tab.Id);
                e.Handled = true;
            }
        }
    }

    private void CloseTab_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is TabItem tab)
        {
            TabCloseRequested?.Invoke(tab.Id);
            e.Handled = true;
        }
    }

    private void NewTabBtn_Click(object sender, RoutedEventArgs e)
    {
        NewTabRequested?.Invoke();
    }
}
