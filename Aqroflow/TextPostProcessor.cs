using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Aqroflow;

/// <summary>
/// Post-processes raw Whisper transcription to produce cleaner, polished text.
/// Two modes:
///   1. Rule-based (always available): regex filler removal, punctuation, self-correction.
///   2. LLM-powered (optional): sends text to a local Ollama instance for intelligent cleanup.
/// </summary>
public class TextPostProcessor
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // ---- filler words / phrases to remove ----
    private static readonly string[] FillerPatterns =
    {
        @"\b(um+|uh+|uhm+|er+|ah+|eh+|hmm+)\b",
        @"\b(like,?\s+)(?=\w)",            // "like" as filler (before a word)
        @"\b(you know)\b",
        @"\b(I mean)\b",
        @"\b(sort of|kind of)\b",
        @"\b(basically)\b",
        @"\b(literally)\b",                // filler usage
        @"\b(right\??\s*,?\s*)(?=\s)",     // trailing "right" as filler
        @"\b(okay so|ok so)\b",
        @"\b(so yeah)\b",
        @"\b(yeah so)\b",
        @"\b(anyway(s)?)\b",
    };

    // compiled regex for performance (case-insensitive)
    private static readonly Regex[] FillerRegexes = FillerPatterns
        .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled))
        .ToArray();

    // ---- self-correction patterns ----
    // Matches: "X actually Y", "X I mean Y", "X no wait Y", "X sorry Y", "X no Y"
    // Keeps only Y (the correction).
    private static readonly Regex SelfCorrectionRegex = new(
        @"(\b\w+(?:\s+\w+){0,5})\s*(?:,\s*)?(?:actually|I mean|no wait|no,?\s|sorry,?\s|wait,?\s|or rather)\s+",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ---- deletion commands ----
    private static readonly Regex DeletionCommandRegex = new(
        @"\b(scratch that|delete that|never mind|forget that|undo that)\b.*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Apply rule-based cleanup to raw transcription text.
    /// </summary>
    public static string CleanRuleBased(string text, StyleConfig? style = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // 1. Handle deletion commands — remove everything from command to end
        text = DeletionCommandRegex.Replace(text, "");

        // 2. Handle self-corrections — keep only the correction
        //    "meet at 2 actually 3" → "meet at 3"
        //    We run this twice to handle chained corrections
        for (int i = 0; i < 2; i++)
            text = SelfCorrectionRegex.Replace(text, "");

        // 3. Remove filler words
        foreach (var rx in FillerRegexes)
            text = rx.Replace(text, " ");

        // 4. Collapse multiple spaces / clean up punctuation artifacts
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"\s+([.,!?;:])", "$1");       // no space before punctuation
        text = Regex.Replace(text, @"([.,!?;:])\s*\1+", "$1");   // deduplicate punctuation
        text = Regex.Replace(text, @",\s*\.", ".");               // ", ." → "."

        // 5. Capitalize first letter of sentences (unless style says no)
        bool capitalize = style?.CapitalizeFirstLetter ?? true;
        text = text.Trim();
        if (capitalize && text.Length > 0)
            text = char.ToUpper(text[0]) + text[1..];

        if (capitalize)
            text = Regex.Replace(text, @"([.!?]\s+)(\w)", m =>
                m.Groups[1].Value + char.ToUpper(m.Groups[2].Value[0]));

        // 6. Ensure the text ends with a period (unless style says no)
        bool addPeriod = style?.AddTrailingPeriod ?? true;
        text = text.TrimEnd();
        if (addPeriod && text.Length > 0 && !".!?".Contains(text[^1]))
            text += ".";

        return text.Trim();
    }

    /// <summary>
    /// Clean text using Ollama LLM for higher quality. Falls back to rule-based on failure.
    /// </summary>
    public static async Task<string> CleanWithOllamaAsync(string text, string endpoint, string model,
        StyleConfig? style = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;

        // first apply rule-based cleanup to give the LLM a head start
        text = CleanRuleBased(text, style);

        try
        {
            var systemPrompt = style?.OllamaSystemPrompt ?? """
                You are a dictation cleanup assistant. Your ONLY job is to clean up speech-to-text output.
                Rules:
                - Remove any remaining filler words (um, uh, like, you know, etc.)
                - Fix grammar and punctuation
                - Handle self-corrections (keep only the final intended meaning)
                - Preserve the EXACT meaning — do NOT add, remove, or change ideas
                - Output ONLY the cleaned text, nothing else — no explanations, no quotes
                - Keep the same language as the input
                - If the input is already clean, output it unchanged
                """;

            var payload = new
            {
                model,
                prompt = text,
                system = systemPrompt,
                stream = false,
                options = new { temperature = 0.1, num_predict = 512 }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync($"{endpoint.TrimEnd('/')}/api/generate", content);
            resp.EnsureSuccessStatusCode();

            var respJson = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respJson);
            var cleaned = doc.RootElement.GetProperty("response").GetString()?.Trim();

            // sanity check: if the LLM returned garbage or empty, fall back
            if (!string.IsNullOrWhiteSpace(cleaned) && cleaned.Length > text.Length / 4)
                return cleaned;
        }
        catch
        {
            // Ollama not running or errored — fall back silently
        }

        return text; // already rule-based cleaned
    }

    /// <summary>
    /// Main entry point: clean text using the best available method per settings.
    /// Optionally applies a context-aware style based on the active app.
    /// </summary>
    public static async Task<string> CleanAsync(string rawText, AppSettings settings,
        StyleConfig? style = null)
    {
        if (!settings.EnableAICleanup)
            return rawText;

        if (!string.IsNullOrWhiteSpace(settings.OllamaModel))
            return await CleanWithOllamaAsync(rawText, settings.OllamaEndpoint, settings.OllamaModel, style);

        return CleanRuleBased(rawText, style);
    }

    /// <summary>
    /// Quick check if Ollama is reachable at the given endpoint.
    /// </summary>
    public static async Task<(bool ok, string message)> TestOllamaAsync(string endpoint, string model)
    {
        try
        {
            var resp = await Http.GetAsync($"{endpoint.TrimEnd('/')}/api/tags");
            resp.EnsureSuccessStatusCode();
            var body = await resp.Content.ReadAsStringAsync();

            if (body.Contains(model, StringComparison.OrdinalIgnoreCase))
                return (true, $"Connected — model '{model}' available.");

            return (false, $"Connected to Ollama, but model '{model}' not found. Run: ollama pull {model}");
        }
        catch (Exception ex)
        {
            return (false, $"Cannot reach Ollama at {endpoint}: {ex.Message}");
        }
    }
}
