using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Aqroflow;

public partial class MainWindow : Window
{
    private enum WidgetState { Loading, Idle, Recording, Transcribing, Error }

    private readonly AudioRecorder _recorder = new();
    private readonly WhisperTranscriber _transcriber = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private WidgetState _state = WidgetState.Loading;

    private Storyboard? _pulseStoryboard;
    private Storyboard? _spinStoryboard;

    // click-vs-drag tracking
    private bool _mouseDown;
    private bool _moved;
    private Point _downScreenPos;
    private double _winStartLeft, _winStartTop;

    public MainWindow()
    {
        InitializeComponent();
        PositionWindow();
        _recorder.AutoStopped += () => Dispatcher.BeginInvoke(StopAndTranscribe);
        _recorder.LevelChanged += level => Dispatcher.BeginInvoke(() => UpdateLevel(level));
        BuildAnimations();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // never steal focus: clicking the widget must leave the caret in the target app
        var handle = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(handle, GWL_EXSTYLE);
        SetWindowLong(handle, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            SetState(WidgetState.Loading);
            var progress = new Progress<int>(p =>
            {
                ProgressText.Text = $"{p}%";
                ProgressText.Visibility = Visibility.Visible;
                MicIcon.Visibility = Visibility.Collapsed;
            });
            await ModelStore.EnsureAsync(progress);
            ProgressText.Visibility = Visibility.Collapsed;
            MicIcon.Visibility = Visibility.Visible;
            await Task.Run(() => _transcriber.Load(ModelStore.ModelPath, _settings.Language));
            SetState(WidgetState.Idle);
        }
        catch (Exception ex)
        {
            SetState(WidgetState.Error);
            Btn.ToolTip = "Aqroflow — failed to load model: " + ex.Message;
        }
    }

    private void PositionWindow()
    {
        var area = SystemParameters.WorkArea;
        Left = _settings.Left ?? area.Right - Width - 24;
        Top = _settings.Top ?? area.Bottom - Height - 24;
    }

    // ---- click / drag ----

    private void Btn_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseDown = true;
        _moved = false;
        _downScreenPos = PointToScreen(e.GetPosition(this));
        _winStartLeft = Left;
        _winStartTop = Top;
        Btn.CaptureMouse();
    }

    private void Btn_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_mouseDown) return;
        var screenPos = PointToScreen(e.GetPosition(this));
        var dxDevice = screenPos.X - _downScreenPos.X;
        var dyDevice = screenPos.Y - _downScreenPos.Y;
        if (!_moved && Math.Abs(dxDevice) < 5 && Math.Abs(dyDevice) < 5) return;
        _moved = true;

        // PointToScreen returns device pixels; Left/Top are DIPs
        var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice;
        double scaleX = transform?.M11 ?? 1.0, scaleY = transform?.M22 ?? 1.0;
        Left = _winStartLeft + dxDevice * scaleX;
        Top = _winStartTop + dyDevice * scaleY;
    }

    private void Btn_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_mouseDown) return;
        _mouseDown = false;
        Btn.ReleaseMouseCapture();
        if (_moved)
        {
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
                _recorder.Start();
                SetState(WidgetState.Recording);
                break;
            case WidgetState.Recording:
                StopAndTranscribe();
                break;
            // Loading/Transcribing: ignore clicks
        }
    }

    private async void StopAndTranscribe()
    {
        if (_state != WidgetState.Recording) return;
        var samples = _recorder.Stop();
        // under ~0.4s of audio is a misclick, not dictation
        if (samples.Length < 16000 * 2 / 5)
        {
            SetState(WidgetState.Idle);
            return;
        }

        SetState(WidgetState.Transcribing);
        try
        {
            var text = await Task.Run(() => _transcriber.TranscribeAsync(samples));
            if (!string.IsNullOrWhiteSpace(text))
                await TextInjector.PasteAsync(text);
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
        // recording pulse: ring expands and fades, repeats
        var opacityAnim = new DoubleAnimation(0.9, 0.0, TimeSpan.FromMilliseconds(900))
        { RepeatBehavior = RepeatBehavior.Forever };
        var scaleXAnim = new DoubleAnimation(1.0, 1.35, TimeSpan.FromMilliseconds(900))
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

        // transcribing spinner
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
        MicIcon.Visibility = Visibility.Visible;

        switch (state)
        {
            case WidgetState.Loading:
                SetGradient("#FF9E9E9E", "#FF616161");
                Btn.ToolTip = "Aqroflow — loading model…";
                break;
            case WidgetState.Idle:
                SetGradient("#FF00C6A7", "#FF0072FF");
                Btn.ToolTip = "Aqroflow — click to dictate";
                break;
            case WidgetState.Recording:
                SetGradient("#FFE53935", "#FFB71C1C");
                Btn.ToolTip = "Aqroflow — recording, click to stop";
                _pulseStoryboard?.Begin();
                break;
            case WidgetState.Transcribing:
                SetGradient("#FF7B1FA2", "#FF303F9F");
                Btn.ToolTip = "Aqroflow — transcribing…";
                MicIcon.Visibility = Visibility.Collapsed;
                Spinner.Visibility = Visibility.Visible;
                _spinStoryboard?.Begin();
                break;
            case WidgetState.Error:
                SetGradient("#FF757575", "#FF424242");
                break;
        }
    }

    private void SetGradient(string c1, string c2)
    {
        Grad1.Color = (Color)ColorConverter.ConvertFromString(c1);
        Grad2.Color = (Color)ColorConverter.ConvertFromString(c2);
    }

    private void UpdateLevel(float rms)
    {
        if (_state != WidgetState.Recording) return;
        // subtle grow with voice level on top of the pulse ring
        double scale = 1.0 + Math.Min(rms * 4, 0.15);
        Btn.RenderTransformOrigin = new Point(0.5, 0.5);
        Btn.RenderTransform = new ScaleTransform(scale, scale);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        _recorder.Stop();
        _transcriber.Dispose();
        Application.Current.Shutdown();
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
