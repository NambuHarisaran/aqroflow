using System.Runtime.InteropServices;
using System.Windows;

namespace Aqroflow;

/// <summary>
/// Injects text at the caret of the currently focused app via clipboard + Ctrl+V.
/// The widget window is WS_EX_NOACTIVATE, so focus never left the target app.
/// Must be called on the UI (STA) thread because of Clipboard access.
/// </summary>
public static class TextInjector
{
    public static async Task PasteAsync(string text)
    {
        string? previous = null;
        try { if (Clipboard.ContainsText()) previous = Clipboard.GetText(); } catch { }

        try { Clipboard.SetDataObject(text, true); } catch { return; }

        await Task.Delay(60); // let clipboard ownership settle before the paste
        SendCtrlV();

        if (previous != null)
        {
            await Task.Delay(600); // give the target app time to read the clipboard
            try { Clipboard.SetDataObject(previous, true); } catch { }
        }
    }

    private const byte VK_CONTROL = 0x11;
    private const byte VK_V = 0x56;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static void SendCtrlV()
    {
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, 0, UIntPtr.Zero);
        keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
