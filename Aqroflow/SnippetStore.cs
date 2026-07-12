using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aqroflow;

/// <summary>
/// Voice-triggered text expansions (snippets).
/// Stored as JSON at %APPDATA%\Aqroflow\snippets.json.
/// When the user says a trigger phrase, it's expanded to the full text.
/// Example: "my email" → "harisaran@example.com"
/// </summary>
public class SnippetStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aqroflow", "snippets.json");

    public List<Snippet> Snippets { get; set; } = new();

    public static SnippetStore Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var snippets = JsonSerializer.Deserialize<List<Snippet>>(json);
                if (snippets != null)
                    return new SnippetStore { Snippets = snippets };
            }
        }
        catch { }
        return new SnippetStore();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Snippets,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>
    /// Scan text for snippet triggers and expand them.
    /// Triggers are matched case-insensitively as whole phrases.
    /// Longer triggers are matched first to avoid partial conflicts.
    /// </summary>
    public string Apply(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Snippets.Count == 0)
            return text;

        // sort by trigger length descending
        var sorted = Snippets.OrderByDescending(s => s.Trigger.Length);
        foreach (var snippet in sorted)
        {
            if (string.IsNullOrWhiteSpace(snippet.Trigger)) continue;
            var pattern = $@"\b{Regex.Escape(snippet.Trigger)}\b";
            text = Regex.Replace(text, pattern, snippet.Expansion ?? "", RegexOptions.IgnoreCase);
        }
        return text;
    }

    public void Add(string trigger, string expansion)
    {
        // remove existing with same trigger
        Snippets.RemoveAll(s => s.Trigger.Equals(trigger.Trim(), StringComparison.OrdinalIgnoreCase));
        Snippets.Add(new Snippet { Trigger = trigger.Trim(), Expansion = expansion });
        Save();
    }

    public void Remove(string trigger)
    {
        Snippets.RemoveAll(s => s.Trigger.Equals(trigger, StringComparison.OrdinalIgnoreCase));
        Save();
    }
}

public class Snippet
{
    public string Trigger { get; set; } = "";
    public string Expansion { get; set; } = "";
}
