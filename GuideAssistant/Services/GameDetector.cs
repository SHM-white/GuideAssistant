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

    /// <summary>
    /// Gets the game name currently in the foreground, or null if no tracked game is active.
    /// </summary>
    public string? CurrentForegroundGameName { get; private set; }

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
    }

    public void ReloadConfigs()
    {
        _configs = _repo.GetAll();
    }

    private void CheckForeground(object? state)
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            var processName = GetProcessName((int)pid);

            if (string.IsNullOrEmpty(processName)) return;

            foreach (var config in _configs)
            {
                if (!config.AutoDetect) continue;
                var gameExe = System.IO.Path.GetFileNameWithoutExtension(config.GamePath);
                var isRunning = string.Equals(processName, gameExe, StringComparison.OrdinalIgnoreCase);

                if (isRunning && !_activeGames.Contains(config.GameName))
                {
                    _activeGames.Add(config.GameName);
                    CurrentForegroundGameName = config.GameName;
                    _launcher.LaunchHelper(config);
                    GameStateChanged?.Invoke(config, true);
                    Log.Information("Game detected: {Game}", config.GameName);
                }
                else if (!isRunning && _activeGames.Contains(config.GameName))
                {
                    _activeGames.Remove(config.GameName);
                    if (CurrentForegroundGameName == config.GameName)
                        CurrentForegroundGameName = null;
                    _launcher.StopHelper(config);
                    GameStateChanged?.Invoke(config, false);
                    Log.Information("Game stopped: {Game}", config.GameName);
                }
                else if (isRunning)
                {
                    CurrentForegroundGameName = config.GameName;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "GameDetector check failed");
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
