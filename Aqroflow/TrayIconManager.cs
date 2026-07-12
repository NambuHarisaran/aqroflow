using System.Drawing;
using System.Windows.Forms;

namespace Aqroflow;

/// <summary>
/// Manages a system tray icon for Aqroflow with context menu
/// and left-click toggle for dictation.
/// Requires <UseWindowsForms>true</UseWindowsForms> in csproj.
/// </summary>
public class TrayIconManager : IDisposable
{
    private NotifyIcon? _tray;
    private readonly MainWindow _widget;

    public TrayIconManager(MainWindow widget)
    {
        _widget = widget;
    }

    public void Install()
    {
        if (_tray != null) return;

        _tray = new NotifyIcon
        {
            Text = "Aqroflow — click to dictate",
            Visible = true
        };

        // use app icon or a built-in icon
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null)
            {
                var ico = Icon.ExtractAssociatedIcon(exe);
                if (ico != null) _tray.Icon = ico;
            }
        }
        catch
        {
            _tray.Icon = SystemIcons.Application;
        }

        // left-click → toggle dictation
        _tray.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _widget.Dispatcher.BeginInvoke(_widget.ExternalToggle);
        };

        // context menu
        var menu = new ContextMenuStrip();
        menu.Items.Add("Dashboard", null, (_, _) =>
            _widget.Dispatcher.BeginInvoke(() => _widget.OpenDashboard("dashboard")));
        menu.Items.Add("Settings", null, (_, _) =>
            _widget.Dispatcher.BeginInvoke(() => _widget.OpenDashboard("settings")));
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) =>
            _widget.Dispatcher.BeginInvoke(() => System.Windows.Application.Current.Shutdown()));

        _tray.ContextMenuStrip = menu;
    }

    public void UpdateTooltip(string text)
    {
        if (_tray != null)
            _tray.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Dispose()
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
    }
}
