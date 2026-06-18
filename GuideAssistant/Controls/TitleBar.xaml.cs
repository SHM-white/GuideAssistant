using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace GuideAssistant.Controls;

public sealed partial class TitleBar : UserControl
{
    public event Action? MinimizeClicked;
    public event Action? MaximizeClicked;
    public event Action? CloseClicked;
    public Slider OpacitySliderControl => OpacitySlider;

    public TitleBar()
    {
        InitializeComponent();
        ApplyHoverTransparency(MinimizeBtn);
        ApplyHoverTransparency(MaximizeBtn);
        ApplyHoverTransparency(CloseBtn);
        ApplyHoverTransparency(OpacityBtn);
    }

    private static void ApplyHoverTransparency(Button btn)
    {
        btn.Opacity = 0.5;
        btn.PointerEntered += (s, e) => btn.Opacity = 1.0;
        btn.PointerExited += (s, e) => btn.Opacity = 0.5;
    }

    private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        => MinimizeClicked?.Invoke();

    private void MaximizeBtn_Click(object sender, RoutedEventArgs e)
        => MaximizeClicked?.Invoke();

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
        => CloseClicked?.Invoke();

    private void OpacityBtn_Click(object sender, RoutedEventArgs e)
    {
        OpacitySlider.Visibility = OpacitySlider.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
