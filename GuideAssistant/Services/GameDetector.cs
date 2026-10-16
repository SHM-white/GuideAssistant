using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GuideAssistant.Data;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Services;

public class GameDetector : IDisposable
{
    private readonly GameRepository _repo;
    private readonly ProcessLauncher _launcher;
    private Timer? _timer;
    private HashSet<string> _activeGames = new();
    private List<GameConfig> _configs = new();
    private bool _isRunning;
    private int _checking;

    /// <summary>
    /// Gets the game name currently in the foreground, or null if no tracked game is active.
    /// </summary>
    public string? CurrentForegroundGameName { get; private set; }
    private GameConfig? _bookmarkGame;

    // Retain the last foreground game while it is running, including when the
    // user gives the toolbar focus to save a bookmark. Hotkeys still use focus.
    public int? BookmarkGameConfigId => Volatile.Read(ref _bookmarkGame)?.Id;

    /// <summary>
    /// Raised on the process scanner's background timer. Subscribers owning
    /// UI/hotkey state should marshal to the UI DispatcherQueue.
    /// Null means no configured game currently has keyboard focus.
    /// </summary>
    public event Action<GameConfig?>? ForegroundGameChanged;
    private int? _foregroundGameConfigId;

    // Callback when game state changes
    public event Action<GameConfig, bool>? GameStateChanged;

    public GameDetector(GameRepository repo, ProcessLauncher launcher)
    {
        _repo = repo;
        _launcher = launcher;
    }

    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _configs = _repo.GetAll();
        _timer = new Timer(CheckForeground, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
        Log.Information("GameDetector started, monitoring {Count} games", _configs.Count);
    }

    public void Stop()
    {
        _isRunning = false;
        _timer?.Dispose();
        _timer = null;
        _launcher.StopAll();
        _activeGames.Clear();
        CurrentForegroundGameName = null;
        _foregroundGameConfigId = null;
        Volatile.Write(ref _bookmarkGame, null);
    }

    public void ReloadConfigs()
    {
        _configs = _repo.GetAll();
    }

    private void CheckForeground(object? state)
    {
        if (!_isRunning || Interlocked.Exchange(ref _checking, 1) != 0) return;
        try
        {
            // Loss of foreground focus is NOT a game exit.
            var hwnd = GetForegroundWindow();
            uint foregroundPid = 0;
            if (hwnd != IntPtr.Zero) GetWindowThreadProcessId(hwnd, out foregroundPid);
            var foregroundProcess = foregroundPid == 0 ? "" : GetProcessName((int)foregroundPid);
            GameConfig? focusedGame = null;
            var runningGameIds = new HashSet<int>();

            foreach (var config in _configs)
            {
                if (!config.AutoDetect) continue;
                var executable = System.IO.Path.GetFileNameWithoutExtension(config.GamePath);
                if (string.IsNullOrWhiteSpace(executable)) continue;

                bool running;
                try
                {
                    var processes = Process.GetProcessesByName(executable);
                    running = processes.Length > 0;
                    foreach (var process in processes) process.Dispose();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Game process query failed for {Exe}", executable);
                    continue;
                }

                if (running) runningGameIds.Add(config.Id);
                if (running && _activeGames.Add(config.GameName))
                {
                    _launcher.LaunchHelper(config);
                    GameStateChanged?.Invoke(config, true);
                    Log.Information("Game process started: {Game}", config.GameName);
                }
                else if (!running && _activeGames.Remove(config.GameName))
                {
                    _launcher.StopHelper(config);
                    GameStateChanged?.Invoke(config, false);
                    Log.Information("Game process exited: {Game}", config.GameName);
                }

                if (running && string.Equals(foregroundProcess, executable, StringComparison.OrdinalIgnoreCase))
                    focusedGame = config;
            }
            CurrentForegroundGameName = focusedGame?.GameName;
            var bookmarkGame = Volatile.Read(ref _bookmarkGame);
            Volatile.Write(ref _bookmarkGame, focusedGame ??
                (bookmarkGame != null && runningGameIds.Contains(bookmarkGame.Id) ? bookmarkGame : null));
            if (_foregroundGameConfigId != focusedGame?.Id)
            {
                _foregroundGameConfigId = focusedGame?.Id;
                ForegroundGameChanged?.Invoke(focusedGame);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GameDetector check failed");
        }
        finally
        {
            Interlocked.Exchange(ref _checking, 0);
        }
    }

    private static string GetProcessName(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.ProcessName;
        }
        catch { return string.Empty; }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    public void Dispose()
    {
        Stop();
    }
}

public class ProcessLauncher
{
    private readonly Dictionary<string, Process> _activeHelpers = new();

    public void LaunchHelper(GameConfig config)
    {
        if (string.IsNullOrEmpty(config.HelperPath)) return;
        if (_activeHelpers.ContainsKey(config.GameName)) return;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = config.HelperPath,
                WorkingDirectory = System.IO.Path.GetDirectoryName(config.HelperPath)
            };
            if (!string.IsNullOrEmpty(config.LaunchArgs))
                psi.Arguments = config.LaunchArgs;

            var proc = Process.Start(psi);
            if (proc != null)
            {
                _activeHelpers[config.GameName] = proc;
                Log.Information("Helper launched: {Helper} for {Game}", config.HelperPath, config.GameName);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch helper for {Game}", config.GameName);
        }
    }

    public void StopHelper(GameConfig config)
    {
        if (_activeHelpers.TryGetValue(config.GameName, out var proc))
        {
            try
            {
                if (!proc.HasExited)
                {
                    proc.Kill(entireProcessTree: true);
                    proc.WaitForExit(3000);
                }
                proc.Dispose();
                Log.Information("Helper stopped for {Game}", config.GameName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to stop helper for {Game}", config.GameName);
            }
            _activeHelpers.Remove(config.GameName);
        }
    }

    public void StopAll()
    {
        foreach (var kvp in _activeHelpers.ToList())
        {
            try
            {
                if (!kvp.Value.HasExited)
                {
                    kvp.Value.Kill(entireProcessTree: true);
                }
                kvp.Value.Dispose();
            }
            catch { }
        }
        _activeHelpers.Clear();
    }
}
