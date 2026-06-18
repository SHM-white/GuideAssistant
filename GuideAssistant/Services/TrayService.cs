using Serilog;

namespace GuideAssistant.Services;

public class TrayService
{
    private readonly MainWindow _mainWindow;

    public TrayService(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void Initialize()
    {
        // In WinUI 3, system tray is handled via the taskbar icon.
        // The app minimizes to taskbar and can be shown/hidden via hotkeys.
        // Full system tray support requires H.NotifyIcon XAML control.
        Log.Information("Tray service initialized (minimize-to-taskbar mode)");
    }
}
