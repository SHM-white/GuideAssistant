using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GuideAssistant.Controls;

public sealed partial class NavigationBar : UserControl
{
    public event Action<string>? NavigateRequested;
    public event Action? BackRequested;
    public event Action? ForwardRequested;
    public event Action? RefreshRequested;
    public event Action? BookmarkRequested;
    public event Action? SettingsRequested;

    public NavigationBar() => InitializeComponent();

    public void SetUrl(string url) => UrlBox.Text = url;
    public void SetBookmarkState(bool isBookmarked)
        => BookmarkIcon.Symbol = isBookmarked ? Symbol.SolidStar : Symbol.OutlineStar;

    private void UrlBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        => NavigateToUrl(args.QueryText);

    private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) { NavigateToUrl(UrlBox.Text); e.Handled = true; }
    }

    private void NavigateToUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        input = input.Trim();
        if (!IsLikelyUrl(input)) input = $"https://www.bing.com/search?q={Uri.EscapeDataString(input)}";
        else if (!input.StartsWith("http://") && !input.StartsWith("https://")) input = $"https://{input}";
        NavigateRequested?.Invoke(input);
    }

    private static bool IsLikelyUrl(string input)
    {
        if (input.StartsWith("http://") || input.StartsWith("https://")) return true;
        if (input.Contains(' ')) return false;
        if (input.Any(c => c >= 0x4E00 && c <= 0x9FFF)) return false;
        if (input.Contains('.')) return true;
        if (input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (Uri.TryCreate($"https://{input}", UriKind.Absolute, out var uri))
            return uri.Host.Contains('.') || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
    private void ForwardBtn_Click(object sender, RoutedEventArgs e) => ForwardRequested?.Invoke();
    private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    private void BookmarkBtn_Click(object sender, RoutedEventArgs e) => BookmarkRequested?.Invoke();
    private void SettingsBtn_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
