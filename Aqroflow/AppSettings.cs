using System.IO;
using System.Text.Json;

namespace Aqroflow;

public class AppSettings
{
    public double? Left { get; set; }
    public double? Top { get; set; }
    public string Language { get; set; } = "en";

    /// <summary>Virtual-key code of the dictation hotkey. Default 0xA3 = Right Ctrl.</summary>
    public int HotkeyVk { get; set; } = 0xA3;

    /// <summary>"pushToTalk" (hold to record) or "toggle" (tap to start/stop).</summary>
    public string HotkeyMode { get; set; } = "pushToTalk";

    /// <summary>Model id from ModelStore.Catalog. Default "tiny.en".</summary>
    public string Model { get; set; } = "tiny.en";

    /// <summary>bottomCenter | bottomRight | bottomLeft | topCenter | topRight | topLeft | custom.</summary>
    public string PositionPreset { get; set; } = "bottomCenter";

    /// <summary>Start Aqroflow automatically on Windows login.</summary>
    public bool LaunchOnStartup { get; set; } = false;

    // ---- Phase 1: AI Cleanup ----

    /// <summary>Enable post-processing of transcriptions (rule-based filler removal, punctuation, etc.).</summary>
    public bool EnableAICleanup { get; set; } = true;

    /// <summary>Ollama model name for LLM-powered cleanup. Empty = rule-based only.</summary>
    public string OllamaModel { get; set; } = "";

    /// <summary>Ollama API endpoint.</summary>
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";

    // ---- Phase 3: UX Polish ----

    /// <summary>Show system tray icon.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Auto-hide (fade) the floating widget after idle.</summary>
    public bool AutoHide { get; set; } = true;

    /// <summary>Seconds of idle before auto-hiding the widget.</summary>
    public int AutoHideDelaySeconds { get; set; } = 10;

    /// <summary>Play start/stop sounds for dictation.</summary>
    public bool PlaySounds { get; set; } = true;

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aqroflow", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
