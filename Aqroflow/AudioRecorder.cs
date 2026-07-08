using NAudio.Wave;

namespace Aqroflow;

/// <summary>
/// Captures mic audio at 16kHz mono (what Whisper expects) and auto-stops
/// after trailing silence once speech has been detected.
/// </summary>
public class AudioRecorder
{
    private WaveInEvent? _waveIn;
    private readonly List<float> _samples = new();
    private readonly object _lock = new();
    private DateTime _startedAt;
    private DateTime _lastVoiceAt;
    private bool _hadSpeech;
    private bool _autoStopRaised;
    private bool _stopOnSilence = true;

    private const float VoiceThreshold = 0.012f;
    private static readonly TimeSpan SilenceStop = TimeSpan.FromSeconds(1.8);
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(90);

    /// <summary>Raised from the audio thread when trailing silence or max duration is hit.</summary>
    public event Action? AutoStopped;

    /// <summary>RMS level of the latest buffer, for UI pulse. Raised from the audio thread.</summary>
    public event Action<float>? LevelChanged;

    public bool IsRecording => _waveIn != null;

    /// <param name="stopOnSilence">
    /// true = auto-stop after trailing silence (click/toggle). false = record until
    /// Stop() is called (push-to-talk hold), with only the max-duration safety cap.
    /// </param>
    public void Start(bool stopOnSilence = true)
    {
        if (_waveIn != null) return;
        lock (_lock) _samples.Clear();
        _hadSpeech = false;
        _autoStopRaised = false;
        _stopOnSilence = stopOnSilence;
        _startedAt = DateTime.UtcNow;
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 50
        };
        _waveIn.DataAvailable += OnData;
        _waveIn.StartRecording();
    }

    public float[] Stop()
    {
        var waveIn = _waveIn;
        _waveIn = null;
        if (waveIn != null)
        {
            waveIn.DataAvailable -= OnData;
            try { waveIn.StopRecording(); } catch { }
            waveIn.Dispose();
        }
        lock (_lock) return _samples.ToArray();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        int sampleCount = e.BytesRecorded / 2;
        if (sampleCount == 0) return;

        float sumSquares = 0;
        lock (_lock)
        {
            for (int i = 0; i < e.BytesRecorded; i += 2)
            {
                short v = BitConverter.ToInt16(e.Buffer, i);
                float f = v / 32768f;
                _samples.Add(f);
                sumSquares += f * f;
            }
        }

        float rms = MathF.Sqrt(sumSquares / sampleCount);
        LevelChanged?.Invoke(rms);

        var now = DateTime.UtcNow;
        if (rms > VoiceThreshold)
        {
            _hadSpeech = true;
            _lastVoiceAt = now;
        }

        bool silenceTimeout = _stopOnSilence && _hadSpeech && now - _lastVoiceAt > SilenceStop;
        bool maxTimeout = now - _startedAt > MaxDuration;
        if ((silenceTimeout || maxTimeout) && !_autoStopRaised)
        {
            _autoStopRaised = true;
            AutoStopped?.Invoke();
        }
    }
}
