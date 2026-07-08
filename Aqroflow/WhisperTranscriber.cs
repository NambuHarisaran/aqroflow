using System.Text;
using System.Text.RegularExpressions;
using Whisper.net;

namespace Aqroflow;

public class WhisperTranscriber : IDisposable
{
    private WhisperFactory? _factory;
    private WhisperProcessor? _processor;

    public bool IsLoaded => _processor != null;

    public void Load(string modelPath, string language)
    {
        // allow switching models: tear down any previously loaded one first
        _processor?.Dispose();
        _factory?.Dispose();
        _processor = null;
        _factory = null;

        _factory = WhisperFactory.FromPath(modelPath);
        _processor = _factory.CreateBuilder()
            .WithLanguage(language)
            .WithThreads(Math.Clamp(Environment.ProcessorCount - 1, 1, 4))
            .Build();
    }

    public async Task<string> TranscribeAsync(float[] samples)
    {
        if (_processor == null) throw new InvalidOperationException("Model not loaded");
        var sb = new StringBuilder();
        await foreach (var segment in _processor.ProcessAsync(samples))
            sb.Append(segment.Text);

        // strip whisper noise tokens like [BLANK_AUDIO], (music), *sigh*
        var text = Regex.Replace(sb.ToString(), @"\[[^\]]*\]|\([^)]*\)|\*[^*]*\*", "");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    public void Dispose()
    {
        _processor?.Dispose();
        _factory?.Dispose();
    }
}
