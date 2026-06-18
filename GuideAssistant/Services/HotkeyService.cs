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
