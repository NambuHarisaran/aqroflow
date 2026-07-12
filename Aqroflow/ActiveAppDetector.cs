using System.Runtime.InteropServices;
using System.Text;

namespace Aqroflow;

/// <summary>
/// Detects the currently active (foreground) application to enable
/// context-aware dictation styles. Uses Win32 APIs to identify the
/// app without requiring any special permissions.
/// </summary>
public static class ActiveAppDetector
{
    public enum AppCategory
    {
        Unknown,
        CodeEditor,     // VS Code, Visual Studio, JetBrains, Cursor, Windsurf
        Chat,           // Slack, Discord, WhatsApp, Telegram, Teams
        Email,          // Outlook, Thunderbird, Gmail (in browser)
        Document,       // Word, Notion, Google Docs (in browser), Obsidian
        Terminal,       // Windows Terminal, PowerShell, CMD, Git Bash
        Browser,        // Chrome, Edge, Firefox, Brave, Arc
        Other
    }

    /// <summary>Returns the title and category of the currently focused window.</summary>
    public static (string title, AppCategory category) GetActiveApp()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return ("", AppCategory.Unknown);

        // get window title
        int len = GetWindowTextLength(hwnd);
        if (len <= 0) return ("", AppCategory.Unknown);
        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        string title = sb.ToString();

        // get process name
        string processName = "";
        try
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            var proc = System.Diagnostics.Process.GetProcessById((int)pid);
            processName = proc.ProcessName.ToLowerInvariant();
        }
        catch { }

        var category = Categorize(title, processName);
        return (title, category);
    }

    private static AppCategory Categorize(string title, string process)
    {
        string t = title.ToLowerInvariant();

        // ---- Code Editors ----
        if (process is "code" or "devenv" or "rider64" or "idea64" or "cursor" or "windsurf"
            or "pycharm64" or "webstorm64" or "goland64" or "clion64"
            or "sublime_text" or "notepad++")
            return AppCategory.CodeEditor;
        if (t.Contains("visual studio code") || t.Contains("- visual studio"))
            return AppCategory.CodeEditor;

        // ---- Terminals ----
        if (process is "windowsterminal" or "powershell" or "pwsh" or "cmd"
            or "wt" or "mintty" or "conhost" or "alacritty" or "wezterm-gui")
            return AppCategory.Terminal;

        // ---- Chat / Messaging ----
        if (process is "slack" or "discord" or "whatsapp" or "telegram" or "teams"
            or "signal" or "element")
            return AppCategory.Chat;
        if (t.Contains("slack") || t.Contains("discord") || t.Contains("whatsapp")
            || t.Contains("telegram") || t.Contains("microsoft teams"))
            return AppCategory.Chat;

        // ---- Email ----
        if (process is "outlook" or "thunderbird")
            return AppCategory.Email;
        if (t.Contains("gmail") || t.Contains("outlook") || t.Contains("mail -"))
            return AppCategory.Email;

        // ---- Document ----
        if (process is "winword" or "obsidian" or "notion")
            return AppCategory.Document;
        if (t.Contains("google docs") || t.Contains("notion") || t.Contains("microsoft word")
            || t.Contains("obsidian"))
            return AppCategory.Document;

        // ---- Browser ----
        if (process is "chrome" or "msedge" or "firefox" or "brave" or "arc" or "opera"
            or "vivaldi")
            return AppCategory.Browser;

        return AppCategory.Other;
    }

    // ---- Win32 ----

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
}
