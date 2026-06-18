using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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
