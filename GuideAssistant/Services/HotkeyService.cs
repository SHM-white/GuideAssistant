using System.Runtime.InteropServices;
using GuideAssistant.Helpers;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Services;

public class HotkeyService : IDisposable
{
    private readonly Dictionary<int, (string action, Action callback)> _bindings = new();
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private GCHandle _gcHandle;
    private bool _disposed;

    /// <summary>Anti-duplicate: tracks last trigger time (ms) per virtual key to prevent
    /// the low-level hook and RegisterHotKey from both firing the same action.</summary>
    private readonly Dictionary<int, long> _lastTriggerTick = new();
    private const long DedupWindowMs = 300;

    /// <summary>Set to true during key-rebind capture to suppress all hotkey actions.</summary>
    public static bool SuppressAll { get; set; }

    /// <summary>Window handle for RegisterHotKey (system-level hotkeys that work across privilege boundaries).</summary>
    private IntPtr _sysHotkeyHwnd;
    private readonly HashSet<int> _registeredSysHotkeyIds = new();

    /// <summary>
    /// Static reference prevents the GC from collecting the delegate thunk
    /// even if the HotkeyService instance were to become unreachable.
    /// </summary>
    private static LowLevelKeyboardProc? s_activeProc;

    public event Action<string>? HotkeyTriggered;

    /// <summary>
    /// Update the full set of hotkey bindings atomically.
    /// </summary>
    public void SetBindings(IEnumerable<HotkeyBinding> bindings)
    {
        _bindings.Clear();
        foreach (var b in bindings)
        {
            if (b.VirtualKey == 0) continue;
            _bindings[b.VirtualKey] = (b.ActionName, () => HotkeyTriggered?.Invoke(b.ActionName));
        }
        Log.Information("Hotkey bindings updated: {Count} active, keys=[{Keys}]",
            _bindings.Count, string.Join(",", _bindings.Keys.Select(k => $"0x{k:X}")));
    }

    /// <summary>
    /// Start the low-level keyboard hook. Must be called from a thread with a message pump.
    /// </summary>
    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        if (_disposed) return;

        _proc = OnLowLevelKeyboardProc;
        s_activeProc = _proc; // Static root prevents GC of the thunk
        _gcHandle = GCHandle.Alloc(_proc);
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, IntPtr.Zero, 0);

        if (_hookId == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            Log.Error("Failed to install keyboard hook (error {Error}). Global hotkeys disabled.", err);
            s_activeProc = null;
        }
        else
        {
            Log.Information("Global keyboard hook installed (id={HookId})", _hookId);
        }
    }

    public void Stop()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
            s_activeProc = null;
            Log.Information("Global keyboard hook stopped");
        }
        if (_gcHandle.IsAllocated)
            _gcHandle.Free();
    }

    private IntPtr OnLowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            int vkCode = Marshal.ReadInt32(lParam);

            if (SuppressAll)
            {
                // During key-rebind capture, suppress all hotkey actions but
                // still pass the key through so the SettingsWindow can capture it.
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            if (_bindings.TryGetValue(vkCode, out var entry))
            {
                if (!TryAcquireTriggerSlot(vkCode))
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);

                Log.Debug("Hotkey triggered: {Action} (VK={Key})", entry.action, vkCode);
                entry.callback();
                return CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
            else if (_bindings.Count > 0)
            {
                Log.Debug("KeyDown VK=0x{Vk:X} ({Vk}) ignored — no matching binding (bindings={Count})",
                    vkCode, vkCode, _bindings.Count);
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Register system-level hotkeys via RegisterHotKey.
    /// These work even when a UAC-elevated window (e.g. Task Manager) has focus,
    /// because RegisterHotKey is not subject to UIPI restrictions.
    /// Called after SetBindings to keep system hotkeys in sync.
    /// </summary>
    public void RegisterSystemHotkeys(IntPtr hwnd)
    {
        UnregisterSystemHotkeys();
        _sysHotkeyHwnd = hwnd;

        // Use id = vkCode (with high bit to avoid collision with WM_HOTKEY convention)
        foreach (var (vk, (action, _)) in _bindings)
        {
            int id = vk;
            if (Win32Helper.RegisterHotKey(hwnd, id, 0, (uint)vk))
            {
                _registeredSysHotkeyIds.Add(id);
                Log.Debug("RegisterHotKey: {Action} id={Id} vk=0x{Vk:X}", action, id, vk);
            }
            else
            {
                Log.Warning("RegisterHotKey failed: {Action} id={Id} vk=0x{Vk:X} (err={Err})",
                    action, id, vk, Marshal.GetLastWin32Error());
            }
        }
    }

    public void UnregisterSystemHotkeys()
    {
        if (_sysHotkeyHwnd == IntPtr.Zero) return;
        foreach (var id in _registeredSysHotkeyIds)
            Win32Helper.UnregisterHotKey(_sysHotkeyHwnd, id);
        _registeredSysHotkeyIds.Clear();
        // Keep _sysHotkeyHwnd so it can be reused by ResumeSystemHotkeysAfterCapture
    }

    /// <summary>Temporarily unregister all system hotkeys during key-rebind capture,
    /// so that RegisterHotKey does not consume the key press before the
    /// ContentDialog can see it.</summary>
    public void SuspendSystemHotkeysForCapture()
    {
        if (_sysHotkeyHwnd == IntPtr.Zero) return;
        UnregisterSystemHotkeys();
        Log.Debug("System hotkeys suspended for key capture (hwnd={Hwnd})", _sysHotkeyHwnd);
    }

    /// <summary>Re-register system hotkeys after key-rebind capture finishes.</summary>
    public void ResumeSystemHotkeysAfterCapture()
    {
        if (_sysHotkeyHwnd == IntPtr.Zero) return;
        foreach (var (vk, (action, _)) in _bindings)
        {
            int id = vk;
            if (Win32Helper.RegisterHotKey(_sysHotkeyHwnd, id, 0, (uint)vk))
            {
                _registeredSysHotkeyIds.Add(id);
            }
            else
            {
                Log.Warning("RegisterHotKey resume failed: {Action} vk=0x{Vk:X}", action, vk);
            }
        }
        Log.Debug("System hotkeys resumed after capture: {Count} registered", _registeredSysHotkeyIds.Count);
    }

    /// <summary>
    /// Called from the main window's WndProc when WM_HOTKEY arrives.
    /// Looks up the binding by virtual key (id) and fires the callback.
    /// </summary>
    public void HandleSystemHotkey(int id)
    {
        if (SuppressAll)
        {
            Log.Debug("System hotkey ignored (SuppressAll=true): id={Id}", id);
            return;
        }

        if (!TryAcquireTriggerSlot(id))
            return;

        if (_bindings.TryGetValue(id, out var entry))
        {
            Log.Debug("System hotkey triggered: {Action} (id={Id})", entry.action, id);
            entry.callback();
        }
    }

    /// <summary>
    /// Returns true if this virtual key hasn't been triggered within the dedup window.
    /// Prevents the low-level hook and RegisterHotKey from double-firing.
    /// </summary>
    private bool TryAcquireTriggerSlot(int vk)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_lastTriggerTick)
        {
            if (_lastTriggerTick.TryGetValue(vk, out var last) && (now - last) < DedupWindowMs)
                return false;
            _lastTriggerTick[vk] = now;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (_sysHotkeyHwnd != IntPtr.Zero)
        {
            foreach (var id in _registeredSysHotkeyIds)
                Win32Helper.UnregisterHotKey(_sysHotkeyHwnd, id);
            _registeredSysHotkeyIds.Clear();
            _sysHotkeyHwnd = IntPtr.Zero;
        }
        _bindings.Clear();
    }

    // ── Known actions & display ──────────────────────────────────

    public static List<(string ActionName, string DisplayName, int DefaultVk)> KnownActions { get; } = new()
    {
        ("play_pause",       "播放/暂停",   0xC0),
        ("fast_forward",     "快进",        0x36),
        ("fast_backward",    "快退",        0x35),
        ("volume_up",        "音量+",       0x39),
        ("volume_down",      "音量-",       0x38),
        ("toggle_visibility","显示/隐藏",   0x48),
        ("bookmark_page",    "收藏页面",    0x42),
        ("toggle_subtitle",  "字幕切换",    0x53),
        ("toggle_minimap",   "小地图切换",  0x4D),
    };

    public static string VirtualKeyToDisplayName(int vk) => vk switch
    {
        0xC0 => "`",
        0x30 => "0", 0x31 => "1", 0x32 => "2", 0x33 => "3", 0x34 => "4",
        0x35 => "5", 0x36 => "6", 0x37 => "7", 0x38 => "8", 0x39 => "9",
        0x41 => "A", 0x42 => "B", 0x43 => "C", 0x44 => "D", 0x45 => "E",
        0x46 => "F", 0x47 => "G", 0x48 => "H", 0x49 => "I", 0x4A => "J",
        0x4B => "K", 0x4C => "L", 0x4D => "M", 0x4E => "N", 0x4F => "O",
        0x50 => "P", 0x51 => "Q", 0x52 => "R", 0x53 => "S", 0x54 => "T",
        0x55 => "U", 0x56 => "V", 0x57 => "W", 0x58 => "X", 0x59 => "Y",
        0x5A => "Z",
        0x70 => "F1", 0x71 => "F2", 0x72 => "F3", 0x73 => "F4",
        0x74 => "F5", 0x75 => "F6", 0x76 => "F7", 0x77 => "F8",
        0x78 => "F9", 0x79 => "F10", 0x7A => "F11", 0x7B => "F12",
        0x2D => "Ins", 0x2E => "Del", 0x24 => "Home", 0x23 => "End",
        0x21 => "PgUp", 0x22 => "PgDn",
        0x27 => "→", 0x25 => "←", 0x26 => "↑", 0x28 => "↓",
        0x20 => "Space", 0x0D => "Enter", 0x1B => "Esc",
        0x09 => "Tab", 0xA0 => "LShift", 0xA1 => "RShift",
        0xA2 => "LCtrl", 0xA3 => "RCtrl", 0xA4 => "LAlt", 0xA5 => "RAlt",
        0xBA => ";", 0xBB => "=", 0xBC => ",", 0xBD => "-", 0xBE => ".",
        0xBF => "/", 0xDB => "[", 0xDC => "\\", 0xDD => "]", 0xDE => "'",
        _ => $"VK(0x{vk:X})"
    };

    // ── P/Invoke ──────────────────────────────────────────────────

    private const int WH_KEYBOARD_LL = 13;
    private static readonly IntPtr WM_KEYDOWN = (IntPtr)0x0100;
    private static readonly IntPtr WM_SYSKEYDOWN = (IntPtr)0x0104;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
