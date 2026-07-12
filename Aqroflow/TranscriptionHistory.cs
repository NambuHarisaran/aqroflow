using System.IO;
using System.Text.Json;

namespace Aqroflow;

/// <summary>
/// Persisted ring-buffer of past transcriptions, stored as JSON
/// at %APPDATA%\Aqroflow\history.json. Max 500 entries.
/// </summary>
public class TranscriptionHistory
{
    private const int MaxEntries = 500;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aqroflow", "history.json");

    public List<HistoryEntry> Entries { get; set; } = new();

    public static TranscriptionHistory Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json);
                if (entries != null)
                    return new TranscriptionHistory { Entries = entries };
            }
        }
        catch { }
        return new TranscriptionHistory();
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

    public void Add(string rawText, string cleanedText, int wordCount, long durationMs)
    {
        Entries.Insert(0, new HistoryEntry
        {
            Timestamp = DateTime.Now,
            RawText = rawText,
            CleanedText = cleanedText,
            WordCount = wordCount,
            DurationMs = durationMs
        });

        // trim to max
        if (Entries.Count > MaxEntries)
            Entries.RemoveRange(MaxEntries, Entries.Count - MaxEntries);

        Save();
    }

    public List<HistoryEntry> Search(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Entries;
        return Entries.Where(e =>
            e.CleanedText.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.RawText.Contains(query, StringComparison.OrdinalIgnoreCase)
        ).ToList();
    }

    public void Clear()
    {
        Entries.Clear();
        Save();
    }
}

public class HistoryEntry
{
    public DateTime Timestamp { get; set; }
    public string RawText { get; set; } = "";
    public string CleanedText { get; set; } = "";
    public int WordCount { get; set; }
    public long DurationMs { get; set; }

    /// <summary>Formatted timestamp for display.</summary>
    public string TimeDisplay => Timestamp.ToString("MMM d, h:mm tt");

    /// <summary>Short preview of the cleaned text.</summary>
    public string Preview => CleanedText.Length > 120
        ? CleanedText[..120] + "…"
        : CleanedText;
}
