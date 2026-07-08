using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Aqroflow;

public partial class MainWindow : Window
{
    private enum WidgetState { Loading, Idle, Recording, Transcribing, Error }

    private const double CircleWidth = 52;
    private const double RecordingWidth = 128;
    private const double HoverScale = 1.14;

    private static readonly Color OrangeColor = (Color)ColorConverter.ConvertFromString("#FFF2A93C");
    private static readonly Color DimColor = (Color)ColorConverter.ConvertFromString("#FF6E6E72");

    private readonly AudioRecorder _recorder = new();
    private readonly WhisperTranscriber _transcriber = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private WidgetState _state = WidgetState.Loading;
    private bool _isHovered;

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
                LogoPath.Visibility = Visibility.Collapsed;
            });
            await ModelStore.EnsureAsync(progress);
            ProgressText.Visibility = Visibility.Collapsed;
            LogoPath.Visibility = Visibility.Visible;
            await Task.Run(() => _transcriber.Load(ModelStore.ModelPath, _settings.Language));
            SetState(WidgetState.Idle);
        }
        catch (Exception ex)
        {
            SetState(WidgetState.Error);
            Pill.ToolTip = "Aqroflow — failed to load model: " + ex.Message;
        }
    }

    private void PositionWindow()
    {
        var area = SystemParameters.WorkArea;
        // default: bottom-center, Wispr Flow style
        Left = _settings.Left ?? area.Left + (area.Width - Width) / 2;
        Top = _settings.Top ?? area.Bottom - Height - 12;
    }

    // ---- hover expand ----

    private void Pill_MouseEnter(object sender, MouseEventArgs e)
    {
        _isHovered = true;
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
                LogoPath.Fill = new SolidColorBrush(OrangeColor);
                SetGlow(Colors.Black, 14, 0.5);
                AnimateWidth(CircleWidth);
                AnimateScale(_isHovered ? HoverScale : 1.0);
                Pill.ToolTip = "Aqroflow — click to dictate";
                break;

            case WidgetState.Recording:
                LogoPath.Visibility = Visibility.Collapsed;
                Waveform.Visibility = Visibility.Visible;
                SetGlow(OrangeColor, 22, 0.75);
                AnimateScale(1.0);
                AnimateWidth(RecordingWidth);
                _pulseStoryboard?.Begin();
                Pill.ToolTip = "Aqroflow — recording, click to stop";
                break;

            case WidgetState.Transcribing:
                LogoPath.Visibility = Visibility.Collapsed;
                Spinner.Visibility = Visibility.Visible;
                SetGlow(OrangeColor, 16, 0.6);
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
