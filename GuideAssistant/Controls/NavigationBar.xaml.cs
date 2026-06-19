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

    public NavigationBar()
    {
        InitializeComponent();
    }

    public void SetUrl(string url)
    {
        UrlBox.Text = url;
    }

    public void SetBookmarkState(bool isBookmarked)
    {
        BookmarkIcon.Symbol = isBookmarked ? Symbol.SolidStar : Symbol.OutlineStar;
    }

    private void UrlBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        NavigateToUrl(args.QueryText);
    }

    private void UrlBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            NavigateToUrl(UrlBox.Text);
            e.Handled = true;
        }
    }

    private void NavigateToUrl(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        input = input.Trim();

        // If not a URL, search via Bing
        if (!IsLikelyUrl(input))
        {
            input = $"https://www.bing.com/search?q={Uri.EscapeDataString(input)}";
        }
        else if (!input.StartsWith("http://") && !input.StartsWith("https://"))
        {
            input = $"https://{input}";
        }

        NavigateRequested?.Invoke(input);
    }

    private static bool IsLikelyUrl(string input)
    {
        // Already has a scheme
        if (input.StartsWith("http://") || input.StartsWith("https://"))
            return true;

        // Contains spaces — definitely a search
        if (input.Contains(' '))
            return false;

        // Contains common CJK characters — likely a search term
        if (input.Any(c => c >= 0x4E00 && c <= 0x9FFF))
            return false;

        // Has a dot and no spaces — likely a domain name
        if (input.Contains('.'))
            return true;

        // localhost or hostname with port
        if (input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        // Try parse as URI to let the system decide
        if (Uri.TryCreate($"https://{input}", UriKind.Absolute, out var uri))
        {
            // Reject if the host portion is non-ASCII (IDN that might be a false positive)
            if (uri.Host.Contains('.'))
                return true;
            // Single-label host: only accept localhost-like
            return uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke();
    }

    private void ForwardBtn_Click(object sender, RoutedEventArgs e)
    {
        ForwardRequested?.Invoke();
    }

    private void RefreshBtn_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke();
    }

    private void BookmarkBtn_Click(object sender, RoutedEventArgs e)
    {
        BookmarkRequested?.Invoke();
    }

    private void SettingsBtn_Click(object sender, RoutedEventArgs e)
    {
        SettingsRequested?.Invoke();
    }
}
