using System.Drawing;
using System.Windows.Forms;

namespace PrismRpc;

public class TrayApp : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly Worker _worker;
    private readonly string _cfgPath;
    private readonly string _logPath;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _presenceItem;
    private readonly System.Windows.Forms.Timer _timer;

    public TrayApp(Worker worker, string cfgPath, string logPath)
    {
        _worker  = worker;
        _cfgPath = cfgPath;
        _logPath = logPath;

        _statusItem = new ToolStripMenuItem("Status: Starting…") { Enabled = false };
        _presenceItem = new ToolStripMenuItem("Presence enabled", null,
            (_, _) => _worker.Paused = !_worker.Paused) { Checked = !_worker.Paused };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_presenceItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open config.json", null, (_, _) => Program.OpenInNotepad(_cfgPath));
        menu.Items.Add("Open prism_rpc.log", null, (_, _) => Program.OpenInNotepad(_logPath));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _icon = new NotifyIcon
        {
            Icon              = LoadIcon(),
            Visible           = true,
            Text              = $"Prism RPC v{Program.Version}",
            ContextMenuStrip  = menu
        };
        _icon.DoubleClick += (_, _) => Program.OpenInNotepad(_cfgPath);

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, _) => Refresh_();
        _timer.Start();
        Refresh_();
    }

    private void Refresh_()
    {
        var st = Helpers.Clip(_worker.Status, 60);
        var desired = $"Status: {st}";
        if (_statusItem.Text != desired) _statusItem.Text = desired;
        _presenceItem.Checked = !_worker.Paused;
    }

    public void RequestStop()
    {
        try { _icon.Visible = false; } catch { }
        ExitThread();
    }

    private void Quit()
    {
        _worker.Stop();
        _worker.Join(3000);
        try { _icon.Visible = false; } catch { }
        _timer.Stop();
        _icon.Dispose();
        ExitThread();
    }

    private static Icon LoadIcon()
    {
        var assetsDir = Path.Combine(AppContext.BaseDirectory, "assets");
        if (Directory.Exists(assetsDir))
        {
            foreach (var name in new[] { "icon.ico", "Icon.ico", "app.ico", "tray.ico", "PrismRPC.ico" })
            {
                var p = Path.Combine(assetsDir, name);
                if (File.Exists(p))
                {
                    try { return new Icon(p); } catch { }
                }
            }
            foreach (var f in Directory.GetFiles(assetsDir, "*.ico"))
            {
                try { return new Icon(f); } catch { }
            }
            foreach (var f in Directory.GetFiles(assetsDir, "*.png"))
            {
                try
                {
                    using var bmp = new Bitmap(f);
                    var h = bmp.GetHicon();
                    using var tmp = Icon.FromHandle(h);
                    return (Icon)tmp.Clone();
                }
                catch { }
            }
        }
        return SystemIcons.Application;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _timer?.Dispose(); } catch { }
            try { _icon.Visible = false; _icon.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}
