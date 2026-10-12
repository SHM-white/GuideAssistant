using System.Runtime.InteropServices;
using GuideAssistant.Models;
using Serilog;

namespace GuideAssistant.Services;

public class HotkeyService : IDisposable
{
    // Replace binding snapshots atomically while the keyboard hook is active.
    private volatile Dictionary<(int Vk, int Modifiers), List<(string action, Action callback)>> _bindings = new();
    private readonly HotkeyPressTracker _pressedKeys = new();
    private IntPtr _hookId = IntPtr.Zero;
    private LowLevelKeyboardProc? _proc;
    private GCHandle _gcHandle;
    private bool _disposed;

    /// <summary>Set to true during key-rebind capture to suppress all hotkey actions.</summary>
    public static bool SuppressAll { get; set; }

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
        var updated = new Dictionary<(int Vk, int Modifiers), List<(string action, Action callback)>>();
        foreach (var b in bindings)
        {
            if (b.VirtualKey == 0) continue;
            var chord = (b.VirtualKey, b.Modifiers & ModifierMask);
            if (!updated.TryGetValue(chord, out var list))
            {
                list = new List<(string, Action)>();
                updated[chord] = list;
            }
            // Multiple actions on a single chord are intentional.
            list.Add((b.ActionName, () => HotkeyTriggered?.Invoke(b.ActionName)));
        }
        _bindings = updated;
        Log.Information("Hotkey bindings updated: {Count} keys, total {Total} actions, keys=[{Keys}]",
            updated.Count, updated.Values.Sum(v => v.Count),
            string.Join(",", updated.Keys.Select(k => $"0x{k.Vk:X}/0x{k.Modifiers:X}")));
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
        _pressedKeys.Reset();
    }

    private IntPtr OnLowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return CallNextHookEx(_hookId, nCode, wParam, lParam);

        try
        {
            if (wParam == WM_KEYUP || wParam == WM_SYSKEYUP)
            {
                _pressedKeys.OnKeyUp(Marshal.ReadInt32(lParam));
            }
            else if (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                // Ignore OS auto-repeat, not a quick second physical press.
                bool firstPress = _pressedKeys.OnKeyDown(vkCode);
                if (firstPress && !SuppressAll)
                {
                    int modifiers = ReadCurrentModifiers() & ~ModifierForKey(vkCode);
                    var snapshot = _bindings;
                    if (snapshot.TryGetValue((vkCode, modifiers), out var entries))
                    {
                        foreach (var entry in entries)
                        {
                            try
                            {
                                Log.Debug("Hotkey triggered: {Action} (VK={Key})", entry.action, vkCode);
                                entry.callback();
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Hotkey action failed: {Action}", entry.action);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Never allow exceptions to cross a native keyboard-hook boundary.
            Log.Error(ex, "Low-level keyboard hook processing failed");
        }

        // Let key events reach the game and the key-binding dialog.
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _bindings = new();
    }

    // ── Modifier support (stored as Ctrl=1, Alt=2, Shift=4, Win=8) ──
    public const int ControlModifier = 1;
    public const int AltModifier = 2;
    public const int ShiftModifier = 4;
    public const int WindowsModifier = 8;
    public const int ModifierMask = ControlModifier | AltModifier | ShiftModifier | WindowsModifier;

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static bool IsModifierKey(int vk) => ModifierForKey(vk) != 0;

    private static int ModifierForKey(int vk) => vk switch
    {
        0x10 or 0xA0 or 0xA1 => ShiftModifier,
        0x11 or 0xA2 or 0xA3 => ControlModifier,
        0x12 or 0xA4 or 0xA5 => AltModifier,
        0x5B or 0x5C => WindowsModifier,
        _ => 0
    };

    public static int ReadCurrentModifiers()
    {
        int modifiers = 0;
        if (IsDown(0x11) || IsDown(0xA2) || IsDown(0xA3)) modifiers |= ControlModifier;
        if (IsDown(0x12) || IsDown(0xA4) || IsDown(0xA5)) modifiers |= AltModifier;
        if (IsDown(0x10) || IsDown(0xA0) || IsDown(0xA1)) modifiers |= ShiftModifier;
        if (IsDown(0x5B) || IsDown(0x5C)) modifiers |= WindowsModifier;
        return modifiers;
    }

    public static string FormatBinding(int vk, int modifiers = 0)
    {
        if (vk == 0) return "未绑定";
        var parts = new List<string>(5);
        if ((modifiers & ControlModifier) != 0) parts.Add("Ctrl");
        if ((modifiers & AltModifier) != 0) parts.Add("Alt");
        if ((modifiers & ShiftModifier) != 0) parts.Add("Shift");
        if ((modifiers & WindowsModifier) != 0) parts.Add("Win");
        parts.Add(VirtualKeyToDisplayName(vk));
        return string.Join("+", parts);
    }

    // ── Known actions & display ──────────────────────────────────

    public static List<(string ActionName, string DisplayName, int DefaultVk)> KnownActions { get; } = new()
    {
        ("play_pause",       "播放/暂停",   0xC0),
        ("fast_forward",     "快进",        0x36),
        ("fast_backward",    "快退",        0x35),
        ("volume_up",        "音量+",       0x38),
        ("volume_down",      "音量-",       0x37),
        ("toggle_visibility","显示/隐藏",   0x30),
        ("bookmark_page",    "收藏页面",    0xDC),
        ("toggle_subtitle",  "字幕切换",    0x39),
        ("toggle_minimap",   "小地图切换",  0x39),
        ("toggle_minimap_filter", "小地图过滤", 0x42),
        ("opacity_up",       "提高透明度",  0xBB),
        ("opacity_down",     "降低透明度",  0xBD),
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
    private static readonly IntPtr WM_KEYUP = (IntPtr)0x0101;
    private static readonly IntPtr WM_SYSKEYUP = (IntPtr)0x0105;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
}
