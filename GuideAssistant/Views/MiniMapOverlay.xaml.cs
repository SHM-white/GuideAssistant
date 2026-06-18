using GuideAssistant.Helpers;
using GuideAssistant.Services;
using Serilog;

namespace GuideAssistant.Views;

public sealed partial class MiniMapOverlay : Window
{
    private readonly DirectionService _directionService;
    private IntPtr _hwnd;
    private readonly Dictionary<string, Shape> _activeArrows = new();
    private readonly Dictionary<string, System.Timers.Timer> _arrowTimers = new();

    public MiniMapOverlay(DirectionService directionService)
    {
        InitializeComponent();
        _directionService = directionService;

        SetupOverlayWindow();
    }

    private void SetupOverlayWindow()
    {
        _hwnd = WindowNative.GetWindowHandle(this);

        var exStyle = Win32Helper.GetWindowLong(_hwnd, Win32Helper.GWL_EXSTYLE);
        Win32Helper.SetWindowLong(_hwnd, Win32Helper.GWL_EXSTYLE,
            exStyle | Win32Helper.WS_EX_TRANSPARENT | Win32Helper.WS_EX_LAYERED | Win32Helper.WS_EX_TOOLWINDOW);
        Win32Helper.SetWindowOpacity(_hwnd, 0.7);
        Win32Helper.SetAlwaysOnTop(_hwnd, true);

        var displayArea = Microsoft.UI.Windowing.DisplayArea.Primary;
        var screenWidth = displayArea.WorkArea.Width;

        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32
        {
            X = (int)(screenWidth - 210),
            Y = 10,
            Width = 200,
            Height = 200
        });

        Log.Information("MiniMapOverlay initialized");
    }

    public void ShowDirection(string directionText)
    {
        var dir = DirectionService.ParseDirection(directionText);
        if (dir == null) return;

        var (angle, label) = dir.Value;

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            DrawArrow(angle, label);
        });
    }

    private void DrawArrow(double angle, string label)
    {
        // Remove previous arrow for same direction
        if (_activeArrows.TryGetValue(label, out var oldArrow))
        {
            ArrowCanvas.Children.Remove(oldArrow);
            _activeArrows.Remove(label);
        }
        if (_arrowTimers.TryGetValue(label, out var oldTimer))
        {
            oldTimer.Stop();
            oldTimer.Dispose();
            lock (_arrowTimers)
            {
                _arrowTimers.Remove(label);
            }
        }

        // Create arrow
        var arrow = new Polygon
        {
            Points = new PointCollection
            {
                new(0, -35),  // tip
                new(-10, 10), // left base
                new(-4, 5),  // left inner
                new(-4, 40), // tail left
                new(4, 40),  // tail right
                new(4, 5),   // right inner
                new(10, 10)  // right base
            },
            Fill = new SolidColorBrush(Colors.LimeGreen),
            Opacity = 0.9
        };

        // Rotate arrow around its own center
        arrow.RenderTransformOrigin = new Windows.Foundation.Point(0.5, 0.5);
        var transform = new RotateTransform { Angle = angle };
        arrow.RenderTransform = transform;

        // Center in canvas (90, 90 is center of 180x180)
        Canvas.SetLeft(arrow, 90);
        Canvas.SetTop(arrow, 90);

        ArrowCanvas.Children.Add(arrow);
        _activeArrows[label] = arrow;

        // Auto remove after 4 seconds
        var timer = new System.Timers.Timer(4000) { AutoReset = false };
        var capturedLabel = label;
        timer.Elapsed += (s, e) =>
        {
            _ = DispatcherQueue.TryEnqueue(() =>
            {
                if (_activeArrows.TryGetValue(capturedLabel, out var capturedArrow))
                {
                    ArrowCanvas.Children.Remove(capturedArrow);
                    _activeArrows.Remove(capturedLabel);
                }
            });
            timer.Dispose();
            lock (_arrowTimers)
            {
                _arrowTimers.Remove(capturedLabel);
            }
        };
        timer.Start();
        lock (_arrowTimers)
        {
            _arrowTimers[label] = timer;
        }
    }
}
