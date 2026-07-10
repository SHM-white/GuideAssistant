using System.Windows.Threading;

namespace GuideAssistant.Overlays;

public class WpfThreadHost : IOverlayController
{
    private Thread? _wpfThread;
    private Dispatcher? _dispatcher;
    private WpfMiniMapOverlay? _miniMap;
    private WpfSubtitleOverlay? _subtitle;
    private readonly Func<string, (double angle, string label)?> _directionParser;
    private volatile bool _isMiniMapVisible;
    private volatile bool _isSubtitleVisible;

    public bool IsMiniMapVisible => _isMiniMapVisible;
    public bool IsSubtitleVisible => _isSubtitleVisible;

    public event Action? MiniMapClosed;
    public event Action? SubtitleClosed;

    public WpfThreadHost(Func<string, (double angle, string label)?> directionParser)
    {
        _directionParser = directionParser;
    }

    public void Start()
    {
        var ready = new ManualResetEventSlim(false);
        _wpfThread = new Thread(() =>
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        });
        _wpfThread.SetApartmentState(ApartmentState.STA);
        _wpfThread.IsBackground = true;
        _wpfThread.Start();
        ready.Wait();
    }

    public void ShowMiniMap()
    {
        _dispatcher?.InvokeAsync(() =>
        {
            if (_miniMap == null)
            {
                _miniMap = new WpfMiniMapOverlay(_directionParser);
                _miniMap.Closed += (s, e) =>
                {
                    _isMiniMapVisible = false;
                    _miniMap = null;
                    MiniMapClosed?.Invoke();
                };
            }
            _miniMap.Show();
            _isMiniMapVisible = true;
        });
    }

    public void HideMiniMap()
    {
        _dispatcher?.InvokeAsync(() =>
        {
            _miniMap?.Close();
            _miniMap = null;
            _isMiniMapVisible = false;
        });
    }

    public void ShowDirection(string directionText)
    {
        _dispatcher?.InvokeAsync(() =>
        {
            _miniMap?.ShowDirection(directionText);
        });
    }

    public void ShowSubtitle()
    {
        _dispatcher?.InvokeAsync(() =>
        {
            if (_subtitle == null)
            {
                _subtitle = new WpfSubtitleOverlay();
                _subtitle.Closed += (s, e) =>
                {
                    _isSubtitleVisible = false;
                    _subtitle = null;
                    SubtitleClosed?.Invoke();
                };
            }
            _subtitle.Show();
            _isSubtitleVisible = true;
        });
    }

    public void HideSubtitle()
    {
        _dispatcher?.InvokeAsync(() =>
        {
            _subtitle?.Close();
            _subtitle = null;
            _isSubtitleVisible = false;
        });
    }

    public void UpdateSubtitle(string text)
    {
        _dispatcher?.InvokeAsync(() =>
        {
            if (_subtitle == null && !string.IsNullOrEmpty(text))
            {
                ShowSubtitle();
            }
            _subtitle?.ShowText(text);
        });
    }

    public void Dispose()
    {
        _dispatcher?.InvokeAsync(() =>
        {
            _miniMap?.Close();
            _subtitle?.Close();
        });

        try
        {
            _dispatcher?.InvokeShutdown();
            _wpfThread?.Join(3000);
        }
        catch { }
    }
}
