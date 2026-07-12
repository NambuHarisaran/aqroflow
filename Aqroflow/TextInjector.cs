using System.Runtime.InteropServices;
using System.Windows;

namespace Aqroflow;

/// <summary>
/// Injects text at the caret of the currently focused app via clipboard + Ctrl+V.
/// The widget window is WS_EX_NOACTIVATE, so focus never left the target app.
/// Must be called on the UI (STA) thread because of Clipboard access.
/// Uses SendInput (modern API) instead of legacy keybd_event for better compatibility.
/// </summary>
public static class TextInjector
{
    private const int MaxClipboardRetries = 3;
    private const int RetryDelayMs = 50;

    public static async Task PasteAsync(string text)
    {
        string? previous = null;
        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); } catch { }

        // retry clipboard set (clipboard can be locked by other apps)
        bool set = false;
        for (int i = 0; i < MaxClipboardRetries && !set; i++)
        {
            try
            {
                Clipboard.SetDataObject(text, true);
                set = true;
            }
            catch
            {
                await Task.Delay(RetryDelayMs);
            }
        }
        if (!set) return;

        await Task.Delay(60); // let clipboard ownership settle before the paste
        SendCtrlV();

        if (previous != null)
        {
            await Task.Delay(800); // give the target app time to read the clipboard
            for (int i = 0; i < MaxClipboardRetries; i++)
            {
                try
                {
                    Clipboard.SetDataObject(previous, true);
                    break;
                }
                catch
                {
                    await Task.Delay(RetryDelayMs);
                }
            }
        }
    }

    // ---- SendInput-based Ctrl+V (modern replacement for keybd_event) ----

    private const ushort VK_CONTROL = 0x11;
    private const ushort VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION union;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private static INPUT MakeKey(ushort vk, bool up) => new()
    {
        type = 1, // INPUT_KEYBOARD
        union = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                dwFlags = up ? KEYEVENTF_KEYUP : 0
            }
        }
    };

    private static void SendCtrlV()
    {
        var inputs = new[]
        {
            MakeKey(VK_CONTROL, false),
            MakeKey(VK_V, false),
            MakeKey(VK_V, true),
            MakeKey(VK_CONTROL, true),
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }
}
