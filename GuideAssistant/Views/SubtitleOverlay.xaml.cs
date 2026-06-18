using GuideAssistant.Helpers;
using GuideAssistant.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;
using WinRT.Interop;

namespace GuideAssistant.Views;

public sealed partial class SubtitleOverlay : Window
{
    private readonly SubtitleService _subtitleService;
    private IntPtr _hwnd;

    private static readonly string[] HighlightWords = {
        "东", "南", "西", "北",
        "左", "右", "上", "下",
        "前方", "后方",
        "左上", "右上", "左下", "右下",
        "东方向", "南方向", "西方向", "北方向"
    };

    public SubtitleOverlay(SubtitleService subtitleService)
    {
        InitializeComponent();
        _subtitleService = subtitleService;

        _subtitleService.SubtitleChanged += OnSubtitleChanged;
        SetupOverlayWindow();
    }

    private void SetupOverlayWindow()
    {
        _hwnd = WindowNative.GetWindowHandle(this);

        // Set window style for overlay
        var exStyle = Win32Helper.GetWindowLong(_hwnd, Win32Helper.GWL_EXSTYLE);
        Win32Helper.SetWindowLong(_hwnd, Win32Helper.GWL_EXSTYLE,
            exStyle | Win32Helper.WS_EX_TRANSPARENT | Win32Helper.WS_EX_LAYERED | Win32Helper.WS_EX_TOOLWINDOW);
        Win32Helper.SetLayeredWindowAttributes(_hwnd, 0, 220, Win32Helper.LWA_ALPHA);
        Win32Helper.SetAlwaysOnTop(_hwnd, true);

        // Position at bottom-center of screen (below center, per requirement)
        var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
        var screenWidth = displayArea.WorkArea.Width;
        var screenHeight = displayArea.WorkArea.Height;

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32
        {
            X = (int)(screenWidth / 2 - 400),
            Y = (int)(screenHeight * 0.65),
            Width = 800,
            Height = 100
        });

        Log.Information("SubtitleOverlay initialized");
    }

    private void OnSubtitleChanged(string text)
    {
        _ = DispatcherQueue.TryEnqueue(() =>
        {
            SubtitleText.Inlines.Clear();

            // Highlight direction words
            var remaining = text;

            while (remaining.Length > 0)
            {
                int earliestIdx = -1;
                string earliestWord = "";

                foreach (var word in HighlightWords)
                {
                    var idx = remaining.IndexOf(word, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0 && (earliestIdx < 0 || idx < earliestIdx))
                    {
                        earliestIdx = idx;
                        earliestWord = word;
                    }
                }

                if (earliestIdx < 0)
                {
                    SubtitleText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = remaining });
                    break;
                }

                // Text before the word
                if (earliestIdx > 0)
                {
                    SubtitleText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = remaining[..earliestIdx] });
                }

                // The highlighted word
                SubtitleText.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
                {
                    Text = remaining[earliestIdx..(earliestIdx + earliestWord.Length)],
                    Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Yellow)
                });

                remaining = remaining[(earliestIdx + earliestWord.Length)..];
            }
        });
    }
}
