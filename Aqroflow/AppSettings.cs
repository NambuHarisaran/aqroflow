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
