using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Aqroflow;

public partial class DashboardWindow : Window
{
    private readonly MainWindow _widget;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _statusTimer;
    private bool _capturingKey;
    private bool _loading = true; // suppress control events during initial populate

    private record Lang(string Code, string Name) { public override string ToString() => Name; }

    private static readonly Lang[] Languages =
    {
        new("en","English"), new("es","Spanish"), new("fr","French"), new("de","German"),
        new("it","Italian"), new("pt","Portuguese"), new("nl","Dutch"), new("ru","Russian"),
        new("hi","Hindi"), new("ta","Tamil"), new("ja","Japanese"), new("zh","Chinese"),
        new("ar","Arabic"), new("ko","Korean"),
    };

    public DashboardWindow(MainWindow widget)
    {
        InitializeComponent();
        _widget = widget;
        _settings = widget.Settings;

        TrySetIcon();

        PopulateFromSettings();
        _widget.StatsChanged += OnStatsChanged;
        OnStatsChanged(_widget.CurrentStats);

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();

        Closed += (_, _) => { _statusTimer.Stop(); _widget.StatsChanged -= OnStatsChanged; };
    }

    private void TrySetIcon()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe == null) return;
            using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            if (ico == null) return;
            Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                ico.Handle, System.Windows.Int32Rect.Empty, null);
        }
        catch { /* window just uses the default icon */ }
    }

    public void Navigate(string page)
    {
        (page == "settings" ? NavSettings : page == "about" ? NavAbout : NavDashboard).IsChecked = true;
    }

    private void PopulateFromSettings()
    {
        VersionText.Text = "v1.0 · free & local";

        // models
        ModelCombo.ItemsSource = ModelStore.Catalog;
        ModelCombo.DisplayMemberPath = "Display";
        ModelCombo.SelectedItem = ModelStore.GetById(_settings.Model);

        // languages
        LangCombo.ItemsSource = Languages;
        LangCombo.SelectedItem = Array.Find(Languages, l => l.Code == _settings.Language) ?? Languages[0];

        // hotkey
        KeyLabel.Text = GlobalHotkey.KeyName(_settings.HotkeyVk);
        ModePush.IsChecked = _settings.HotkeyMode != "toggle";
        ModeToggle.IsChecked = _settings.HotkeyMode == "toggle";
        HintText.Text = _settings.HotkeyMode == "toggle"
            ? $"or tap {GlobalHotkey.KeyName(_settings.HotkeyVk)} anywhere"
            : $"or hold {GlobalHotkey.KeyName(_settings.HotkeyVk)} anywhere";

        StartupCheck.IsChecked = _settings.LaunchOnStartup;
        StatModel.Text = _settings.Model;
        UpdateLangEnabled();
        UpdateModelStatus();
        _loading = false;
    }

    private void UpdateLangEnabled()
    {
        var model = ModelCombo.SelectedItem as ModelInfo ?? ModelStore.GetById(_settings.Model);
        LangCombo.IsEnabled = model.Multilingual;
    }

    private void UpdateModelStatus()
    {
        var model = ModelCombo.SelectedItem as ModelInfo ?? ModelStore.GetById(_settings.Model);
        ModelStatus.Text = ModelStore.IsDownloaded(model) ? "downloaded" : $"not downloaded ({model.SizeLabel})";
    }

    // ---- navigation ----

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageDashboard == null) return;
        PageDashboard.Visibility = NavDashboard.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageAbout.Visibility = NavAbout.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---- dashboard ----

    private void RecordBtn_Click(object sender, RoutedEventArgs e) => _widget.ExternalToggle();

    private void RefreshStatus()
    {
        bool rec = _widget.IsRecording;
        RecordBtn.Content = rec ? "Stop dictation" : "Start dictation";
        StatusDot.Text = _widget.StatusText;
    }

    private void OnStatsChanged(MainWindow.Stats s)
    {
        StatCount.Text = s.Count.ToString();
        StatWords.Text = s.Words.ToString();
        LastText.Text = string.IsNullOrEmpty(s.Last) ? "—" : s.Last;
    }

    // ---- settings: hotkey ----

    private void ChangeKey_Click(object sender, RoutedEventArgs e)
    {
        _capturingKey = true;
        KeyLabel.Text = "press a key…";
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_capturingKey)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            int vk = KeyInterop.VirtualKeyFromKey(key);
            _settings.HotkeyVk = vk;
            _settings.Save();
            _capturingKey = false;
            KeyLabel.Text = GlobalHotkey.KeyName(vk);
            _widget.ReloadHotkey();
            UpdateHint();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.HotkeyMode = ModeToggle.IsChecked == true ? "toggle" : "pushToTalk";
        _settings.Save();
        _widget.ReloadHotkey();
        UpdateHint();
    }

    private void UpdateHint()
    {
        string key = GlobalHotkey.KeyName(_settings.HotkeyVk);
        HintText.Text = _settings.HotkeyMode == "toggle" ? $"or tap {key} anywhere" : $"or hold {key} anywhere";
    }

    // ---- settings: model ----

    private void ModelCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        UpdateLangEnabled();
        UpdateModelStatus();
    }

    private async void ApplyModel_Click(object sender, RoutedEventArgs e)
    {
        if (ModelCombo.SelectedItem is not ModelInfo model) return;
        var lang = (LangCombo.SelectedItem as Lang)?.Code ?? "en";

        _settings.Model = model.Id;
        _settings.Language = lang;
        _settings.Save();

        ApplyModelBtn.IsEnabled = false;
        ModelProgress.Visibility = Visibility.Visible;
        ModelProgress.Value = 0;
        ModelStatus.Text = ModelStore.IsDownloaded(model) ? "loading…" : "downloading…";

        var progress = new Progress<int>(p => ModelProgress.Value = p);
        try
        {
            await _widget.ReloadModelAsync(progress);
            ModelStatus.Text = "ready";
            StatModel.Text = model.Id;
        }
        catch (Exception ex)
        {
            ModelStatus.Text = "failed: " + ex.Message;
        }
        finally
        {
            ModelProgress.Visibility = Visibility.Collapsed;
            ApplyModelBtn.IsEnabled = true;
        }
    }

    // ---- settings: position ----

    private void Position_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && b.Tag is string preset)
        {
            _widget.ApplyPositionPreset(preset);
            PositionNote.Text = $"Widget moved to {preset}. You can also drag it anywhere.";
        }
    }

    // ---- settings: startup ----

    private void Startup_Click(object sender, RoutedEventArgs e)
    {
        bool enable = StartupCheck.IsChecked == true;
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key != null)
            {
                if (enable)
                    key.SetValue("Aqroflow", $"\"{Environment.ProcessPath}\"");
                else
                    key.DeleteValue("Aqroflow", throwOnMissingValue: false);
            }
            _settings.LaunchOnStartup = enable;
            _settings.Save();
        }
        catch
        {
            StartupCheck.IsChecked = !enable; // revert on failure
        }
    }
}
