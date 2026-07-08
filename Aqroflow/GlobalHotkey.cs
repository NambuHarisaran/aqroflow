using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Aqroflow;

/// <summary>
/// System-wide keyboard hook for a single push-to-talk / toggle key.
/// Must be constructed and installed on the UI thread (it has the message pump
/// the low-level hook callback runs on), so events fire on the UI thread.
/// </summary>
public class GlobalHotkey : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hook = IntPtr.Zero;
    private bool _down;

    public int Vk { get; set; }

    /// <summary>Key pressed (first down; auto-repeat is suppressed).</summary>
    public event Action? Pressed;
    /// <summary>Key released.</summary>
    public event Action? Released;

    public GlobalHotkey(int vk)
    {
        Vk = vk;
        _proc = HookCallback;
    }

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        using var proc = Process.GetCurrentProcess();
        using var module = proc.MainModule!;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == Vk)
            {
                int msg = (int)wParam;
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    if (!_down) { _down = true; Pressed?.Invoke(); }
                }
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    if (_down) { _down = false; Released?.Invoke(); }
                }
            }
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    /// <summary>Human-readable name for common virtual-key codes.</summary>
    public static string KeyName(int vk) => vk switch
    {
        0xA2 => "Left Ctrl",
        0xA3 => "Right Ctrl",
        0xA0 => "Left Shift",
        0xA1 => "Right Shift",
        0xA4 => "Left Alt",
        0xA5 => "Right Alt",
        0x20 => "Space",
        0x13 => "Pause",
        0x91 => "Scroll Lock",
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),
        _ => "key 0x" + vk.ToString("X2")
    };

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
