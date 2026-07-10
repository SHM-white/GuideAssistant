using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GuideAssistant.Overlays;

public partial class WpfMiniMapOverlay : WpfOverlayBase
{
    private readonly Func<string, (double angle, string label)?> _directionParser;
    private readonly Dictionary<string, Shape> _activeArrows = new();
    private readonly Dictionary<string, DispatcherTimer> _arrowTimers = new();

    public WpfMiniMapOverlay(Func<string, (double angle, string label)?> directionParser)
    {
        InitializeComponent();
        _directionParser = directionParser;

        var screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = screenWidth / 100.0;
        Top = screenWidth / 100.0;
    }

    public void ShowDirection(string directionText)
    {
        if (!CheckAccess())
        {
            Dispatcher.InvokeAsync(() => ShowDirection(directionText));
            return;
        }

        var dir = _directionParser(directionText);
        if (dir == null) return;
        var (angle, label) = dir.Value;
        DrawArrow(angle, label);
    }

    private void DrawArrow(double angle, string label)
    {
        if (_activeArrows.TryGetValue(label, out var oldArrow))
        {
            ArrowCanvas.Children.Remove(oldArrow);
            _activeArrows.Remove(label);
        }
        if (_arrowTimers.TryGetValue(label, out var oldTimer))
        {
            oldTimer.Stop();
            _arrowTimers.Remove(label);
        }

        var arrow = new Polygon
        {
            Points = new PointCollection
            {
                new Point(0, -35),
                new Point(-10, 10),
                new Point(-4, 5),
                new Point(-4, 40),
                new Point(4, 40),
                new Point(4, 5),
                new Point(10, 10)
            },
            Fill = new SolidColorBrush(Colors.LimeGreen),
            Opacity = 0.9,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(angle)
        };

        Canvas.SetLeft(arrow, 90);
        Canvas.SetTop(arrow, 90);

        ArrowCanvas.Children.Add(arrow);
        _activeArrows[label] = arrow;

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        var capturedLabel = label;
        timer.Tick += (s, e) =>
        {
            timer.Stop();
            if (_activeArrows.TryGetValue(capturedLabel, out var a))
            {
                ArrowCanvas.Children.Remove(a);
                _activeArrows.Remove(capturedLabel);
            }
            _arrowTimers.Remove(capturedLabel);
        };
        timer.Start();
        _arrowTimers[label] = timer;
    }
}
