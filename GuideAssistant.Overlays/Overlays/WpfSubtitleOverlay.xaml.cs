using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GuideAssistant.Overlays;

public partial class WpfSubtitleOverlay : WpfOverlayBase
{
    private static readonly string[] HighlightWords = {
        "东", "南", "西", "北",
        "左", "右", "上", "下",
        "前方", "后方",
        "左上", "右上", "左下", "右下",
        "东方向", "南方向", "西方向", "北方向"
    };

    public WpfSubtitleOverlay()
    {
        InitializeComponent();

        var screenWidth = SystemParameters.PrimaryScreenWidth;
        var screenHeight = SystemParameters.PrimaryScreenHeight;
        Left = screenWidth / 2.0 - 400;
        Top = screenHeight * 0.9;
    }

    public void ShowText(string text)
    {
        if (!CheckAccess())
        {
            Dispatcher.InvokeAsync(() => ShowText(text));
            return;
        }

        SubtitleText.Inlines.Clear();
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
                SubtitleText.Inlines.Add(new Run(remaining));
                break;
            }

            if (earliestIdx > 0)
            {
                SubtitleText.Inlines.Add(new Run(remaining[..earliestIdx]));
            }

            SubtitleText.Inlines.Add(new Run(remaining[earliestIdx..(earliestIdx + earliestWord.Length)])
            {
                Foreground = Brushes.Yellow
            });

            remaining = remaining[(earliestIdx + earliestWord.Length)..];
        }
    }
}
