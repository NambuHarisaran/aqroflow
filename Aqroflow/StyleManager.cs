using System.IO;
using System.Text.Json;
using static Aqroflow.ActiveAppDetector;

namespace Aqroflow;

/// <summary>
/// Manages context-aware dictation styles. When the user dictates,
/// the style is selected based on the active app category and
/// adjusts how the text cleanup behaves.
/// </summary>
public class StyleManager
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aqroflow", "styles.json");

    /// <summary>Per-category style configuration.</summary>
    public Dictionary<string, StyleConfig> Styles { get; set; } = new();

    public StyleManager()
    {
        // defaults
        if (Styles.Count == 0) ResetDefaults();
    }

    public void ResetDefaults()
    {
        Styles = new()
        {
            ["CodeEditor"] = new StyleConfig
            {
                Name = "Technical",
                Description = "Preserves technical terms, uses camelCase/snake_case when appropriate",
                OllamaSystemPrompt = "You are a dictation cleanup assistant for code editors. Keep technical jargon, function names, and variable-style naming intact. Use precise, concise language. Remove fillers. Output only the cleaned text."
            },
            ["Chat"] = new StyleConfig
            {
                Name = "Casual",
                Description = "Relaxed tone, no periods at end of short messages",
                AddTrailingPeriod = false,
                CapitalizeFirstLetter = false,
                OllamaSystemPrompt = "You are a dictation cleanup assistant for casual chat messages. Keep the tone relaxed and informal. Remove fillers but don't over-formalize. Don't add periods to short messages. Output only the cleaned text."
            },
            ["Email"] = new StyleConfig
            {
                Name = "Formal",
                Description = "Professional tone, proper punctuation",
                OllamaSystemPrompt = "You are a dictation cleanup assistant for professional emails. Use formal, polished language with proper grammar and punctuation. Remove fillers. Output only the cleaned text."
            },
            ["Document"] = new StyleConfig
            {
                Name = "Document",
                Description = "Clean prose, well-punctuated paragraphs",
                OllamaSystemPrompt = "You are a dictation cleanup assistant for document writing. Produce clean, well-structured prose with proper punctuation and paragraph-ready formatting. Remove fillers. Output only the cleaned text."
            },
            ["Terminal"] = new StyleConfig
            {
                Name = "Command",
                Description = "Minimal formatting, command-oriented",
                AddTrailingPeriod = false,
                CapitalizeFirstLetter = false,
                OllamaSystemPrompt = "You are a dictation cleanup assistant for terminal/command-line use. Output concise, command-oriented text. Don't add punctuation or capitalization. Remove fillers. Output only the cleaned text."
            },
            ["Browser"] = new StyleConfig
            {
                Name = "Standard",
                Description = "Default clean formatting",
                OllamaSystemPrompt = null
            },
            ["Other"] = new StyleConfig
            {
                Name = "Standard",
                Description = "Default clean formatting",
                OllamaSystemPrompt = null
            }
        };
    }

    /// <summary>Get the style config for the given app category.</summary>
    public StyleConfig GetStyle(AppCategory category)
    {
        string key = category.ToString();
        return Styles.TryGetValue(key, out var style) ? style : new StyleConfig();
    }

    /// <summary>Get the Ollama system prompt override for the active app, or null for default.</summary>
    public string? GetOllamaPrompt(AppCategory category)
    {
        var style = GetStyle(category);
        return style.OllamaSystemPrompt;
    }

    public static StyleManager Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var styles = JsonSerializer.Deserialize<Dictionary<string, StyleConfig>>(json);
                if (styles != null && styles.Count > 0)
                    return new StyleManager { Styles = styles };
            }
        }
        catch { }
        return new StyleManager();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Styles,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}

public class StyleConfig
{
    public string Name { get; set; } = "Standard";
    public string Description { get; set; } = "Default formatting";
    public bool CapitalizeFirstLetter { get; set; } = true;
    public bool AddTrailingPeriod { get; set; } = true;
    /// <summary>If non-null, overrides the default Ollama system prompt for this style.</summary>
    public string? OllamaSystemPrompt { get; set; }
}
