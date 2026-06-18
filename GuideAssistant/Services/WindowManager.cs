using GuideAssistant.Data;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Services;

public class WindowManager
{
    private readonly WindowStateRepository _repo;

    public IntPtr MainWindowHandle { get; set; }

    public WindowManager(WindowStateRepository repo)
    {
        _repo = repo;
    }

    public WindowState? LoadState(string windowName)
    {
        return _repo.Get(windowName);
    }

    public void SaveState(WindowState state)
    {
        _repo.Save(state);
        Log.Information("Window state saved: {Name} ({X},{Y} {W}x{H} Opacity:{O})",
            state.WindowName, state.X, state.Y, state.Width, state.Height, state.Opacity);
    }

    public void SetOpacity(IntPtr hWnd, double opacity)
    {
        opacity = Math.Clamp(opacity, 0.1, 1.0);
        Helpers.Win32Helper.SetWindowOpacity(hWnd, opacity);
    }

    public void SetAlwaysOnTop(IntPtr hWnd, bool onTop)
    {
        Helpers.Win32Helper.SetAlwaysOnTop(hWnd, onTop);
    }
}
