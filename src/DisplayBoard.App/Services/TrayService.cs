using System.Drawing;
using System.Windows.Forms;

namespace DisplayBoard.App.Services;

/// <summary>Icon khay hệ thống. App vẫn trình chiếu khi cửa sổ chính bị ẩn.</summary>
public sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _start;
    private readonly ToolStripMenuItem _stop;
    private readonly ToolStripLabel _title;
    private string _name = "Display Board";
    private bool _running;

    public TrayService(Action open, Action start, Action stop, Action exit)
    {
        _start = new ToolStripMenuItem("Bắt đầu trình chiếu", null, (_, _) => start());
        _stop = new ToolStripMenuItem("Dừng trình chiếu", null, (_, _) => stop());
        var menu = new ContextMenuStrip();
        _title = new ToolStripLabel(_name) { Font = new Font(SystemFonts.MenuFont ?? SystemFonts.DefaultFont, FontStyle.Bold) };
        menu.Items.Add(_title);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Mở ứng dụng", null, (_, _) => open()));
        menu.Items.Add(_start);
        menu.Items.Add(_stop);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Thoát", null, (_, _) => exit()));

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = _name,
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
        _running = running;
        UpdateText();
    }

    /// <summary>Đổi tên hiển thị (khách tự đặt ở tab Cài đặt).</summary>
    public void SetName(string name)
    {
        _name = name;
        _title.Text = name;
        UpdateText();
    }

    // Tooltip của NotifyIcon tối đa 127 ký tự.
    private void UpdateText()
    {
        var text = _running ? $"{_name} – đang trình chiếu" : _name;
        _icon.Text = text.Length > 127 ? text[..127] : text;
    }

    public void ShowBalloon(string text) => _icon.ShowBalloonTip(3000, _name, text, ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
