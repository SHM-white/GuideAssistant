using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GuideAssistant.Helpers;
using Serilog;

namespace GuideAssistant.Services;

public class HotkeyService : IDisposable
{
    private IntPtr _hwnd;
    private int _nextId = 1000;
    private readonly Dictionary<int, (string action, Action callback)> _hotkeys = new();

    public void Initialize(IntPtr hwnd)
    {
        _hwnd = hwnd;
        Log.Information("HotkeyService initialized with HWND: {Hwnd}", hwnd);
    }

    public int RegisterHotkey(string action, uint modifiers, uint virtualKey, Action callback)
    {
        int id = _nextId++;
        if (Win32Helper.RegisterHotKey(_hwnd, id, modifiers, virtualKey))
        {
            _hotkeys[id] = (action, callback);
            Log.Information("Hotkey registered: {Action} (Modifiers={Modifiers}, Key={Key}) => ID={Id}",
                action, modifiers, virtualKey, id);
            return id;
        }
        Log.Warning("Failed to register hotkey: {Action} (Modifiers={Modifiers}, Key={Key})", action, modifiers, virtualKey);
        return -1;
    }

    public bool UnregisterHotkey(int id)
    {
        if (_hotkeys.Remove(id))
        {
            Win32Helper.UnregisterHotKey(_hwnd, id);
            return true;
        }
        return false;
    }

    public IReadOnlyList<int> GetRegisteredIds() => _hotkeys.Keys.ToList();

    public bool HandleHotkey(int id)
    {
        if (_hotkeys.TryGetValue(id, out var entry))
        {
            entry.callback();
            return true;
        }
        return false;
    }

    public const uint VK_OEM_3 = 0xC0; // ` key
    public const uint VK_5 = 0x35;
    public const uint VK_6 = 0x36;
    public const uint VK_8 = 0x38;
    public const uint VK_9 = 0x39;
    public const uint VK_H = 0x48;
    public const uint VK_D = 0x44;
    public const uint VK_S = 0x53;
    public const uint VK_M = 0x4D;

    /// <summary>
    /// Converts a Win32 virtual-key code to a human-readable key name.
    /// </summary>
    public static string VirtualKeyToDisplayName(uint vk)
    {
        return vk switch
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
    }

    /// <summary>
    /// Returns all known hotkey actions with their display names and default key suggestions.
    /// </summary>
    public static List<(string ActionName, string DisplayName, uint DefaultVk)> KnownActions { get; } = new()
    {
        ("play_pause", "播放/暂停", 0xC0),
        ("fast_forward", "快进", 0x36),
        ("fast_backward", "快退", 0x35),
        ("volume_up", "音量+", 0x39),
        ("volume_down", "音量-", 0x38),
        ("toggle_visibility", "显示/隐藏", 0),
        ("bookmark_page", "收藏页面", 0),
        ("toggle_subtitle", "字幕切换", 0x53),
        ("toggle_minimap", "小地图切换", 0x4D),
    };

    private const int WH_KEYBOARD_LL = 13;
    private IntPtr _hookId = IntPtr.Zero;
    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    private LowLevelKeyboardProc? _proc;
    private GCHandle _gcHandle;

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    public void StartLowLevelHook(Action<int> onKeyDown)
    {
        if (_hookId != IntPtr.Zero) return;
        _proc = (nCode, wParam, lParam) =>
        {
            if (nCode >= 0 && wParam == (IntPtr)0x0100) // WM_KEYDOWN
            {
                int vkCode = Marshal.ReadInt32(lParam);
                onKeyDown(vkCode);
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        };
        _gcHandle = GCHandle.Alloc(_proc);
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        if (_hookId == IntPtr.Zero)
            Log.Warning("LowLevel keyboard hook failed");
    }

    public void StopLowLevelHook()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
        if (_gcHandle.IsAllocated)
            _gcHandle.Free();
    }

    public void Dispose()
    {
        StopLowLevelHook();
        foreach (var id in _hotkeys.Keys.ToList())
        {
            Win32Helper.UnregisterHotKey(_hwnd, id);
        }
        _hotkeys.Clear();
    }
}
