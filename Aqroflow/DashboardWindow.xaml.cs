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
        var nav = page switch
        {
            "settings" => NavSettings,
            "about" => NavAbout,
            "dictionary" => NavDictionary,
            "snippets" => NavSnippets,
            "history" => NavHistory,
            _ => NavDashboard
        };
        nav.IsChecked = true;
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

        // experience settings
        TrayIconCheck.IsChecked = _settings.ShowTrayIcon;
        PlaySoundsCheck.IsChecked = _settings.PlaySounds;
        AutoHideCheck.IsChecked = _settings.AutoHide;
        AutoHideDelayBox.Text = _settings.AutoHideDelaySeconds.ToString();

        // AI Cleanup
        CleanupCheck.IsChecked = _settings.EnableAICleanup;
        OllamaModelBox.Text = _settings.OllamaModel;
        OllamaEndpointBox.Text = _settings.OllamaEndpoint;

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
        if (_loading || PageDashboard == null) return;
        var btn = sender as System.Windows.Controls.RadioButton;
        if (btn?.IsChecked != true) return;

        PageDashboard.Visibility = Visibility.Collapsed;
        PageSettings.Visibility = Visibility.Collapsed;
        PageDictionary.Visibility = Visibility.Collapsed;
        PageSnippets.Visibility = Visibility.Collapsed;
        PageHistory.Visibility = Visibility.Collapsed;
        PageAbout.Visibility = Visibility.Collapsed;

        UIElement? activePage = null;

        if (btn == NavDashboard) activePage = PageDashboard;
        else if (btn == NavSettings) activePage = PageSettings;
        else if (btn == NavDictionary) activePage = PageDictionary;
        else if (btn == NavSnippets) activePage = PageSnippets;
        else if (btn == NavHistory) activePage = PageHistory;
        else if (btn == NavAbout) activePage = PageAbout;

        if (activePage != null)
        {
            activePage.Visibility = Visibility.Visible;
            
            // smooth fade-in and subtle slide-up animation
            activePage.Opacity = 0;
            activePage.RenderTransform = new System.Windows.Media.TranslateTransform(0, 8);

            var sb = new System.Windows.Media.Animation.Storyboard();
            
            var fade = new System.Windows.Media.Animation.DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
            System.Windows.Media.Animation.Storyboard.SetTarget(fade, activePage);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(fade, new PropertyPath("Opacity"));
            
            var slide = new System.Windows.Media.Animation.DoubleAnimation(0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
            };
            System.Windows.Media.Animation.Storyboard.SetTarget(slide, activePage);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(slide, new PropertyPath("RenderTransform.Y"));

            sb.Children.Add(fade);
            sb.Children.Add(slide);
            sb.Begin();
        }

        // refresh data lists when switching to those pages
        if (NavDictionary.IsChecked == true) RefreshDictList();
        if (NavSnippets.IsChecked == true) RefreshSnipList();
        if (NavHistory.IsChecked == true) RefreshHistoryList();
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
        StatCount.Text = $"in {s.Count} sessions";
        StatWords.Text = $"{s.Words} words";
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

    // ---- settings: experience ----

    private void Experience_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.ShowTrayIcon = TrayIconCheck.IsChecked == true;
        _settings.PlaySounds = PlaySoundsCheck.IsChecked == true;
        _settings.AutoHide = AutoHideCheck.IsChecked == true;
        _settings.Save();
    }

    private void AutoHideDelay_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        if (int.TryParse(AutoHideDelayBox.Text, out int sec) && sec > 0)
        {
            _settings.AutoHideDelaySeconds = sec;
            _settings.Save();
        }
    }

    // ---- settings: AI cleanup ----

    private void Cleanup_Click(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        _settings.EnableAICleanup = CleanupCheck.IsChecked == true;
        _settings.Save();
    }

    private void OllamaModel_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        _settings.OllamaModel = OllamaModelBox.Text.Trim();
        _settings.Save();
    }

    private void OllamaEndpoint_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading) return;
        _settings.OllamaEndpoint = OllamaEndpointBox.Text.Trim();
        _settings.Save();
    }

    private async void TestOllama_Click(object sender, RoutedEventArgs e)
    {
        TestOllamaBtn.IsEnabled = false;
        OllamaStatus.Text = "Testing…";
        var model = OllamaModelBox.Text.Trim();
        var endpoint = OllamaEndpointBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            OllamaStatus.Text = "Enter a model name first (e.g. llama3.2:1b).";
            TestOllamaBtn.IsEnabled = true;
            return;
        }
        var (ok, msg) = await TextPostProcessor.TestOllamaAsync(endpoint, model);
        OllamaStatus.Foreground = ok
            ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF4CAF50"))
            : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFFF5252"));
        OllamaStatus.Text = msg;
        TestOllamaBtn.IsEnabled = true;
    }

    // ---- dictionary ----

    private void RefreshDictList()
    {
        DictList.ItemsSource = null;
        DictList.ItemsSource = _widget.Dictionary.Entries.ToList();
    }

    private void DictAdd_Click(object sender, RoutedEventArgs e)
    {
        var spoken = DictSpokenBox.Text.Trim();
        var replace = DictReplaceBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(spoken) || string.IsNullOrWhiteSpace(replace)) return;
        _widget.Dictionary.Add(spoken, replace);
        DictSpokenBox.Text = "";
        DictReplaceBox.Text = "";
        RefreshDictList();
    }

    private void DictRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.Tag is string key)
        {
            _widget.Dictionary.Remove(key);
            RefreshDictList();
        }
    }

    // ---- snippets ----

    private void RefreshSnipList()
    {
        SnipList.ItemsSource = null;
        SnipList.ItemsSource = _widget.Snippets.Snippets;
    }

    private void SnipAdd_Click(object sender, RoutedEventArgs e)
    {
        var trigger = SnipTriggerBox.Text.Trim();
        var expansion = SnipExpansionBox.Text;
        if (string.IsNullOrWhiteSpace(trigger) || string.IsNullOrWhiteSpace(expansion)) return;
        _widget.Snippets.Add(trigger, expansion);
        SnipTriggerBox.Text = "";
        SnipExpansionBox.Text = "";
        RefreshSnipList();
    }

    private void SnipRemove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.Tag is string trigger)
        {
            _widget.Snippets.Remove(trigger);
            RefreshSnipList();
        }
    }

    // ---- history ----

    private void RefreshHistoryList(string? filter = null)
    {
        HistoryList.ItemsSource = null;
        HistoryList.ItemsSource = string.IsNullOrWhiteSpace(filter)
            ? _widget.History.Entries
            : _widget.History.Search(filter);
    }

    private void HistorySearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        RefreshHistoryList(HistorySearchBox.Text);
    }

    private void HistoryClear_Click(object sender, RoutedEventArgs e)
    {
        _widget.History.Clear();
        RefreshHistoryList();
    }

    private void HistoryItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Border border && border.Tag is string text)
        {
            try { Clipboard.SetDataObject(text, true); } catch { }
        }
    }
}
