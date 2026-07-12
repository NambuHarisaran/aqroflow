using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aqroflow;

/// <summary>
/// A user-maintained dictionary of word/phrase replacements.
/// Stored as JSON at %APPDATA%\Aqroflow\dictionary.json.
/// Applied after Whisper transcription to fix recurring misrecognitions
/// (e.g., "gee pee tee" → "GPT", "aqro flow" → "Aqroflow").
/// </summary>
public class PersonalDictionary
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aqroflow", "dictionary.json");

    /// <summary>Key = phrase as spoken (lowercase), Value = replacement text.</summary>
    public Dictionary<string, string> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static PersonalDictionary Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var entries = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (entries != null)
                    return new PersonalDictionary
                    {
                        Entries = new Dictionary<string, string>(entries, StringComparer.OrdinalIgnoreCase)
                    };
            }
        }
        catch { }
        return new PersonalDictionary();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Entries,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>
    /// Apply all dictionary replacements to the given text (case-insensitive, whole-word).
    /// Replacements are applied longest-first to avoid partial matches.
    /// </summary>
    public string Apply(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Entries.Count == 0)
            return text;

        // sort by length descending so longer phrases match first
        var sorted = Entries.OrderByDescending(kv => kv.Key.Length);
        foreach (var (spoken, replacement) in sorted)
        {
            // whole-word match, case-insensitive
            var pattern = $@"\b{Regex.Escape(spoken)}\b";
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }
        return text;
    }

    public void Add(string spoken, string replacement)
    {
        Entries[spoken.Trim()] = replacement.Trim();
        Save();
    }

    public void Remove(string spoken)
    {
        Entries.Remove(spoken);
        Save();
    }
}
