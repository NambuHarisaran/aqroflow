using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Media;

namespace Aqroflow;

public partial class MainWindow : Window
{
    private enum WidgetState { Loading, Idle, Recording, Transcribing, Error }

    private const double CircleWidth = 54;
    private const double RecordingWidth = 128;
    private const double HoverScale = 1.14;

    private static readonly Color OrangeColor = (Color)ColorConverter.ConvertFromString("#FFF5A623");
    private static readonly Color DimColor = (Color)ColorConverter.ConvertFromString("#FF6E6E72");

    private readonly AudioRecorder _recorder = new();
    private readonly WhisperTranscriber _transcriber = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly PersonalDictionary _dictionary = PersonalDictionary.Load();
    private readonly SnippetStore _snippets = SnippetStore.Load();
    private readonly TranscriptionHistory _history = TranscriptionHistory.Load();
    private readonly StyleManager _styles = StyleManager.Load();
    private ActiveAppDetector.AppCategory _activeAppCategory = ActiveAppDetector.AppCategory.Unknown;
    private GlobalHotkey? _hotkey;
    private TrayIconManager? _tray;
    private DashboardWindow? _dashboard;
    private WidgetState _state = WidgetState.Loading;
    private bool _isHovered;
    private DispatcherTimer? _autoHideTimer;
    private DateTime _lastActivityTime = DateTime.UtcNow;

    // ---- public API used by the dashboard window ----

    public struct Stats { public int Count; public int Words; public string Last; }

    public AppSettings Settings => _settings;
    public PersonalDictionary Dictionary => _dictionary;
    public SnippetStore Snippets => _snippets;
    public TranscriptionHistory History => _history;
    public StyleManager Styles => _styles;
    public Stats CurrentStats { get; private set; } = new() { Last = "" };
    public event Action<Stats>? StatsChanged;
    public bool IsRecording => _state == WidgetState.Recording;
    public string StatusText => _state switch
    {
        WidgetState.Loading => "● loading model…",
        WidgetState.Idle => "● ready",
        WidgetState.Recording => "● recording",
        WidgetState.Transcribing => "● transcribing…",
        _ => "● error"
    };

    private ModelInfo CurrentModel => ModelStore.GetById(_settings.Model);
    private string EffectiveLanguage => CurrentModel.Multilingual ? _settings.Language : "en";

    private Storyboard? _pulseStoryboard;
    private Storyboard? _spinStoryboard;

    private Rectangle[] _bars = Array.Empty<Rectangle>();
    private double[] _barLevels = Array.Empty<double>();

    // click-vs-drag tracking
    private bool _mouseDown;
    private bool _moved;
    private Point _downScreenPos;
    private double _winStartLeft, _winStartTop;

    public MainWindow()
    {
        InitializeComponent();
        PositionWindow();
        _bars = Waveform.Children.OfType<Rectangle>().ToArray();
        _barLevels = new double[_bars.Length];
        _recorder.AutoStopped += () => Dispatcher.BeginInvoke(StopAndTranscribe);
        _recorder.LevelChanged += level => Dispatcher.BeginInvoke(() => UpdateLevel(level));
        BuildAnimations();
        BuildContextMenu();
        SetupAutoHide();
        SetupTray();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // never steal focus: clicking the widget must leave the caret in the target app
        var handle = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);

        // system-wide dictation key
        _hotkey = new GlobalHotkey(_settings.HotkeyVk);
        _hotkey.Pressed += () => Dispatcher.BeginInvoke(OnHotkeyPressed);
        _hotkey.Released += () => Dispatcher.BeginInvoke(OnHotkeyReleased);
        _hotkey.Install();
    }

    private void OnHotkeyPressed()
    {
        if (_settings.HotkeyMode == "toggle")
        {
            OnMicClicked();
        }
        else // pushToTalk: hold to record, no silence auto-stop
        {
            if (_state == WidgetState.Idle)
            {
                ResetAutoHide();
                PlayStartSound();
                _activeAppCategory = ActiveAppDetector.GetActiveApp().category;
                _recorder.Start(stopOnSilence: false);
                SetState(WidgetState.Recording);
            }
        }
    }

    private void OnHotkeyReleased()
    {
        if (_settings.HotkeyMode == "pushToTalk" && _state == WidgetState.Recording)
            StopAndTranscribe();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
        => await ReloadModelAsync(null);

    /// <summary>
    /// Downloads (if needed) and loads the model selected in settings, showing
    /// progress on the widget and forwarding it to an optional external listener
    /// (the dashboard). Safe to call repeatedly to switch models.
    /// </summary>
    public async Task ReloadModelAsync(IProgress<int>? external)
    {
        try
        {
            SetState(WidgetState.Loading);
            var model = CurrentModel;
            var progress = new Progress<int>(p =>
            {
                ProgressText.Text = $"{p}%";
                ProgressText.Visibility = Visibility.Visible;
                LogoPath.Visibility = Visibility.Collapsed;
                external?.Report(p);
            });
            await ModelStore.EnsureAsync(model, progress);
            ProgressText.Visibility = Visibility.Collapsed;
            LogoPath.Visibility = Visibility.Visible;
            await Task.Run(() => _transcriber.Load(ModelStore.PathFor(model), EffectiveLanguage));
            SetState(WidgetState.Idle);
        }
        catch (Exception ex)
        {
            SetState(WidgetState.Error);
            Pill.ToolTip = "Aqroflow — failed to load model: " + ex.Message;
            throw;
        }
    }

    public void ReloadHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = new GlobalHotkey(_settings.HotkeyVk);
        _hotkey.Pressed += () => Dispatcher.BeginInvoke(OnHotkeyPressed);
        _hotkey.Released += () => Dispatcher.BeginInvoke(OnHotkeyReleased);
        _hotkey.Install();
        if (_state == WidgetState.Idle) Pill.ToolTip = IdleTooltip();
    }

    /// <summary>Start/stop dictation from outside the widget (dashboard button).</summary>
    public void ExternalToggle() => OnMicClicked();

    private void PositionWindow()
    {
        if (_settings.PositionPreset != "custom")
        {
            var (l, t) = ComputePreset(_settings.PositionPreset);
            Left = l; Top = t;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = _settings.Left ?? area.Left + (area.Width - Width) / 2;
            Top = _settings.Top ?? area.Bottom - Height - 12;
        }
    }

    private (double left, double top) ComputePreset(string preset)
    {
        var a = SystemParameters.WorkArea;
        const double m = 16;
        double cx = a.Left + (a.Width - Width) / 2;
        return preset switch
        {
            "bottomRight" => (a.Right - Width - m, a.Bottom - Height - m),
            "bottomLeft" => (a.Left + m, a.Bottom - Height - m),
            "topCenter" => (cx, a.Top + m),
            "topRight" => (a.Right - Width - m, a.Top + m),
            "topLeft" => (a.Left + m, a.Top + m),
            _ => (cx, a.Bottom - Height - 12), // bottomCenter
        };
    }

    /// <summary>Move the floating widget to a named screen anchor and persist it.</summary>
    public void ApplyPositionPreset(string preset)
    {
        _settings.PositionPreset = preset;
        var (l, t) = ComputePreset(preset);
        Left = l; Top = t;
        _settings.Left = l; _settings.Top = t;
        _settings.Save();
    }

    // ---- hover expand ----

    private void Pill_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
        ResetAutoHide();
        if (_state == WidgetState.Idle) AnimateScale(HoverScale);
    }

    private void Pill_MouseLeave(object sender, MouseEventArgs e)
    {
        _isHovered = false;
        if (_state == WidgetState.Idle) AnimateScale(1.0);
    }

    private void AnimateScale(double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        PillScale.BeginAnimation(ScaleTransform.ScaleYProperty, anim.Clone());
    }

    private void AnimateWidth(double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(200))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        Pill.BeginAnimation(WidthProperty, anim);
    }

    // ---- click / drag ----

    private void Pill_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = true;
        _moved = false;
        _downScreenPos = PointToScreen(e.GetPosition(this));
        _winStartLeft = Left;
        _winStartTop = Top;
        Pill.CaptureMouse();
    }

    private void Pill_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown) return;
        var screenPos = PointToScreen(e.GetPosition(this));
        var dxDevice = screenPos.X - _downScreenPos.X;
        var dyDevice = screenPos.Y - _downScreenPos.Y;
        if (!_moved && Math.Abs(dxDevice) < 5 && Math.Abs(dyDevice) < 5) return;
        _moved = true;

        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        double scaleX = transform?.M11 ?? 1.0, scaleY = transform?.M22 ?? 1.0;
        Left = _winStartLeft + dxDevice * scaleX;
        Top = _winStartTop + dyDevice * scaleY;
    }

    private void Pill_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = false;
        Pill.ReleaseMouseCapture();
        if (_moved)
        {
            _settings.PositionPreset = "custom";
            _settings.Left = Left;
            _settings.Top = Top;
            _settings.Save();
        }
        else
        {
            OnMicClicked();
        }
    }

    // ---- recording flow ----

    private void OnMicClicked()
    {
        switch (_state)
        {
            case WidgetState.Idle:
                ResetAutoHide();
                PlayStartSound();
                _activeAppCategory = ActiveAppDetector.GetActiveApp().category;
                _recorder.Start();
                SetState(WidgetState.Recording);
                break;
            case WidgetState.Recording:
                StopAndTranscribe();
                break;
        }
    }

    private async void StopAndTranscribe()
    {
        if (_state != WidgetState.Recording) return;
        var recordingStart = DateTime.UtcNow;
        var samples = _recorder.Stop();

        // sound feedback: stop chime
        if (_settings.PlaySounds)
            SystemSounds.Asterisk.Play();

        // under ~0.4s of audio is a misclick, not dictation
        if (samples.Length < 16000 * 2 / 5)
        {
            SetState(WidgetState.Idle);
            return;
        }

        SetState(WidgetState.Transcribing);
        try
        {
            var rawText = await Task.Run(() => _transcriber.TranscribeAsync(samples));
            if (!string.IsNullOrWhiteSpace(rawText))
            {
                // AI cleanup: remove fillers, fix punctuation, handle self-corrections
                // Context-aware: style varies by active app (chat=casual, email=formal, etc.)
                var style = _styles.GetStyle(_activeAppCategory);
                var text = await TextPostProcessor.CleanAsync(rawText, _settings, style);

                // Personal dictionary: fix recurring misrecognitions
                text = _dictionary.Apply(text);

                // Snippets: expand voice shortcuts
                text = _snippets.Apply(text);

                await TextInjector.PasteAsync(text);

                // Record in history
                var durationMs = (long)(DateTime.UtcNow - recordingStart).TotalMilliseconds;
                var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                _history.Add(rawText, text, wordCount, durationMs);

                var s = CurrentStats;
                s.Count++;
                s.Words += wordCount;
                s.Last = text;
                CurrentStats = s;
                StatsChanged?.Invoke(s);
            }
        }
        catch
        {
            // transcription failure: swallow, return to idle
        }
        SetState(WidgetState.Idle);
    }

    // ---- visual states ----

    private void BuildAnimations()
    {
        var opacityAnim = new DoubleAnimation(0.9, 0.0, TimeSpan.FromMilliseconds(1000))
        { RepeatBehavior = RepeatBehavior.Forever };
        var scaleXAnim = new DoubleAnimation(1.0, 1.5, TimeSpan.FromMilliseconds(1000))
        { RepeatBehavior = RepeatBehavior.Forever };
        var scaleYAnim = scaleXAnim.Clone();

        Storyboard.SetTarget(opacityAnim, PulseRing);
        Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(OpacityProperty));
        Storyboard.SetTarget(scaleXAnim, PulseRing);
        Storyboard.SetTargetProperty(scaleXAnim, new PropertyPath("RenderTransform.ScaleX"));
        Storyboard.SetTarget(scaleYAnim, PulseRing);
        Storyboard.SetTargetProperty(scaleYAnim, new PropertyPath("RenderTransform.ScaleY"));

        _pulseStoryboard = new Storyboard();
        _pulseStoryboard.Children.Add(opacityAnim);
        _pulseStoryboard.Children.Add(scaleXAnim);
        _pulseStoryboard.Children.Add(scaleYAnim);

        var spinAnim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(800))
        { RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(spinAnim, Spinner);
        Storyboard.SetTargetProperty(spinAnim, new PropertyPath("RenderTransform.Angle"));
        _spinStoryboard = new Storyboard();
        _spinStoryboard.Children.Add(spinAnim);
    }

    private void SetState(WidgetState state)
    {
        _state = state;
        _pulseStoryboard?.Stop();
        _spinStoryboard?.Stop();
        PulseRing.Opacity = 0;

        Spinner.Visibility = Visibility.Collapsed;
        Waveform.Visibility = Visibility.Collapsed;
        LogoPath.Visibility = Visibility.Visible;

        switch (state)
        {
            case WidgetState.Loading:
                LogoPath.Fill = new SolidColorBrush(DimColor);
                AnimateWidth(CircleWidth);
                Pill.ToolTip = "Aqroflow — loading model…";
                break;

            case WidgetState.Idle:
                LogoPath.Fill = (System.Windows.Media.Brush)FindResource("AccentGrad");
                SetGlow(Colors.Black, 18, 0.4);
                AnimateWidth(CircleWidth);
                AnimateScale(_isHovered ? HoverScale : 1.0);
                Pill.ToolTip = IdleTooltip();
                break;

            case WidgetState.Recording:
                LogoPath.Visibility = Visibility.Collapsed;
                Waveform.Visibility = Visibility.Visible;
                SetGlow(OrangeColor, 22, 0.6);
                AnimateScale(1.0);
                AnimateWidth(RecordingWidth);
                _pulseStoryboard?.Begin();
                Pill.ToolTip = "Aqroflow — recording, click to stop";
                break;

            case WidgetState.Transcribing:
                LogoPath.Visibility = Visibility.Collapsed;
                Spinner.Visibility = Visibility.Visible;
                SetGlow(OrangeColor, 16, 0.5);
                AnimateScale(1.0);
                AnimateWidth(CircleWidth);
                _spinStoryboard?.Begin();
                Pill.ToolTip = "Aqroflow — transcribing…";
                break;

            case WidgetState.Error:
                LogoPath.Fill = new SolidColorBrush(DimColor);
                AnimateWidth(CircleWidth);
                break;
        }
    }

    private void SetGlow(Color color, double blur, double opacity)
    {
        PillShadow.Color = color;
        PillShadow.BlurRadius = blur;
        PillShadow.Opacity = opacity;
    }

    private void UpdateLevel(float rms)
    {
        if (_state != WidgetState.Recording || _bars.Length == 0) return;
        // map rms to bar heights with a center-weighted profile + smoothing
        double norm = Math.Min(rms * 6.5, 1.0);
        int mid = _bars.Length / 2;
        for (int i = 0; i < _bars.Length; i++)
        {
            double distFromCenter = 1.0 - Math.Abs(i - mid) / (double)(mid + 1);
            double target = 5 + norm * 22 * distFromCenter;
            _barLevels[i] += (target - _barLevels[i]) * 0.5; // smooth
            _bars[i].Height = Math.Max(4, _barLevels[i]);
        }
    }

    // ---- context menu ----

    private void BuildContextMenu()
    {
        var dash = new MenuItem { Header = "Open Dashboard" };
        dash.Click += (_, _) => OpenDashboard("dashboard");
        var settings = new MenuItem { Header = "Settings" };
        settings.Click += (_, _) => OpenDashboard("settings");
        var exit = new MenuItem { Header = "Exit Aqroflow" };
        exit.Click += Exit_Click;

        var menu = new ContextMenu();
        menu.Items.Add(dash);
        menu.Items.Add(settings);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        Pill.ContextMenu = menu;
    }

    public void OpenDashboard(string page)
    {
        if (_dashboard == null)
        {
            _dashboard = new DashboardWindow(this);
            _dashboard.Closed += (_, _) => _dashboard = null;
        }
        _dashboard.Navigate(page);
        if (!_dashboard.IsVisible) _dashboard.Show();
        _dashboard.Activate();
    }

    private string IdleTooltip()
    {
        string key = GlobalHotkey.KeyName(_settings.HotkeyVk);
        string gesture = _settings.HotkeyMode == "toggle" ? $"tap {key}" : $"hold {key}";
        return $"Aqroflow — click or {gesture} to dictate";
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _hotkey?.Dispose();
        _tray?.Dispose();
        _autoHideTimer?.Stop();
        _recorder.Stop();
        _transcriber.Dispose();
        Application.Current.Shutdown();
    }

    // ---- auto-hide ----

    private void SetupAutoHide()
    {
        _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _autoHideTimer.Tick += (_, _) =>
        {
            if (!_settings.AutoHide || _state != WidgetState.Idle || _isHovered) return;
            if ((DateTime.UtcNow - _lastActivityTime).TotalSeconds >= _settings.AutoHideDelaySeconds)
                AnimateOpacity(0.15);
        };
        _autoHideTimer.Start();
    }

    private void ResetAutoHide()
    {
        _lastActivityTime = DateTime.UtcNow;
        if (Opacity < 1.0)
            AnimateOpacity(1.0);
    }

    private void AnimateOpacity(double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(400))
        { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, anim);
    }

    // ---- tray icon ----

    private void SetupTray()
    {
        if (!_settings.ShowTrayIcon) return;
        _tray = new TrayIconManager(this);
        _tray.Install();
    }

    // ---- sound feedback ----

    private void PlayStartSound()
    {
        if (_settings.PlaySounds)
            SystemSounds.Exclamation.Play();
    }

    // ---- win32 ----

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
}
