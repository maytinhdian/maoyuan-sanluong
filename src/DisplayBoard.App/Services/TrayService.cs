using System.Drawing;
using System.Windows.Forms;

namespace DisplayBoard.App.Services;

/// <summary>Icon khay hệ thống. App vẫn trình chiếu khi cửa sổ chính bị ẩn.</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _start;
    private readonly ToolStripMenuItem _stop;

    public TrayService(Action open, Action start, Action stop, Action exit)
    {
        _start = new ToolStripMenuItem("Bắt đầu trình chiếu", null, (_, _) => start());
        _stop = new ToolStripMenuItem("Dừng trình chiếu", null, (_, _) => stop());
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripLabel("Display Board") { Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold) });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Mở ứng dụng", null, (_, _) => open()));
        menu.Items.Add(_start);
        menu.Items.Add(_stop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Thoát", null, (_, _) => exit()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Display Board",
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => open();
        SetRunning(false);
    }

    public void SetRunning(bool running)
    {
        _start.Enabled = !running;
        _stop.Enabled = running;
        _icon.Text = running ? "Display Board – đang trình chiếu" : "Display Board";
    }

    public void ShowBalloon(string text) => _icon.ShowBalloonTip(3000, "Display Board", text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
