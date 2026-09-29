using System.Drawing;
using System.Runtime.InteropServices;
namespace P1Z.Subtitler.Services;

internal sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;
    private readonly System.Windows.Forms.ToolStripMenuItem _enabledItem;
    private readonly Icon _enabledIcon;
    private readonly Icon _disabledIcon;
    private readonly IntPtr _enabledIconHandle;
    private readonly IntPtr _disabledIconHandle;

    public TrayService(Action showSettings, Action toggleEnabled, Action exit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip
        {
            BackColor = Color.FromArgb(0x0C, 0x0F, 0x11),
            ForeColor = Color.FromArgb(0xDD, 0xE4, 0xE2),
            ShowImageMargin = false,
            RenderMode = System.Windows.Forms.ToolStripRenderMode.System
        };
        var settingsItem = new System.Windows.Forms.ToolStripMenuItem("Settings");
        settingsItem.Click += (_, _) => showSettings();
        _enabledItem = new System.Windows.Forms.ToolStripMenuItem("Enabled") { CheckOnClick = false };
        _enabledItem.Click += (_, _) => toggleEnabled();
        var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => exit();
        menu.Items.Add(settingsItem);
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(exitItem);

        _enabledIconHandle = CreateIconHandle(Color.FromArgb(0xEE, 0xFF, 0xFF, 0xFF));
        _disabledIconHandle = CreateIconHandle(Color.FromArgb(0x88, 0x8C, 0x92, 0x96));
        _enabledIcon = Icon.FromHandle(_enabledIconHandle);
        _disabledIcon = Icon.FromHandle(_disabledIconHandle);

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Text = "p1z.subtitler",
            Icon = _enabledIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => showSettings();
    }

    public void SetEnabled(bool enabled)
    {
        _enabledItem.Checked = enabled;
        _enabledItem.Text = enabled ? "Disable" : "Enable";
        _icon.Text = enabled ? "p1z.subtitler / enabled" : "p1z.subtitler / disabled";
        _icon.Icon = enabled ? _enabledIcon : _disabledIcon;
    }

    /// <summary>
    /// The subtitle in miniature: a dark rectangle with two text bars, so the tray
    /// entry reads as this app rather than as a generic executable.
    /// </summary>
    private static IntPtr CreateIconHandle(Color ink)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);
            using var fill = new SolidBrush(Color.FromArgb(0xD8, 0, 0, 0));
            graphics.FillRectangle(fill, new Rectangle(1, 5, 30, 22));

            using var text = new SolidBrush(ink);
            graphics.FillRectangle(text, 6, 11, 20, 3);
            graphics.FillRectangle(text, 6, 18, 13, 3);
        }

        // The handle is owned by this service and released in Dispose; Icon.FromHandle
        // does not take ownership of it.
        return bitmap.GetHicon();
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _enabledIcon.Dispose();
        _disabledIcon.Dispose();
        DestroyIcon(_enabledIconHandle);
        DestroyIcon(_disabledIconHandle);
    }
}
