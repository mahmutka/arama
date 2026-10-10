using System;
using System.Drawing;
using System.Windows.Forms;
using Arama.App;

namespace Arama.UI
{
    sealed class TrayIcon : IDisposable
    {
        readonly NotifyIcon notify = new NotifyIcon();
        readonly ToolStripMenuItem startupItem = new ToolStripMenuItem("Windows ile başlat");

        public TrayIcon(MainForm form, Icon icon)
        {
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("Göster", null, (s, e) => form.ShowAndFocus()));
            startupItem.Click += (s, e) => ToggleStartup();
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Çıkış", null, (s, e) => form.ExitApplication()));
            menu.Opening += (s, e) => startupItem.Checked = StartupManager.IsEnabled(StartupManager.TaskName);

            notify.Icon = icon;
            notify.Text = "Arama";
            notify.ContextMenuStrip = menu;
            notify.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) form.ShowAndFocus(); };
            notify.Visible = true;
        }

        void ToggleStartup()
        {
            bool ok = startupItem.Checked
                ? StartupManager.Disable(StartupManager.TaskName)
                : StartupManager.Enable(StartupManager.TaskName, Application.ExecutablePath);
            if (!ok) ShowWarning("Windows ile başlatma ayarı değiştirilemedi.");
        }

        public void ShowWarning(string text)
        {
            notify.ShowBalloonTip(5000, "Arama", text, ToolTipIcon.Warning);
        }

        public void Dispose()
        {
            notify.Visible = false;
            notify.Dispose();
        }
    }

    static class Hotkey
    {
        public const int Id = 0xA7A;

        /// <summary>Ctrl+Alt+Space; başka program almışsa false.</summary>
        public static bool Register(IntPtr hwnd)
        {
            return Native.RegisterHotKey(hwnd, Id, Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT, (uint)Keys.Space);
        }

        public static void Unregister(IntPtr hwnd)
        {
            Native.UnregisterHotKey(hwnd, Id);
        }
    }
}
