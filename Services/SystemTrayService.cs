using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace SoundBoard.Services;

/// <summary>
/// Manages the Windows System Tray NotifyIcon, Context Menu, and Startup Toggle.
/// </summary>
public class SystemTrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startupMenuItem;

    public event Action? OnOpenManagerRequested;
    public event Action? OnRescanRequested;
    public event Action? OnExitRequested;

    public SystemTrayService()
    {
        _startupMenuItem = new ToolStripMenuItem("Run at Windows Startup", null, OnStartupToggled)
        {
            Checked = Helpers.StartupManager.IsStartupEnabled
        };

        var contextMenu = new ContextMenuStrip();
        contextMenu.Items.Add(new ToolStripMenuItem("🎵 Open Audio & Preset Manager...", null, (s, e) => OnOpenManagerRequested?.Invoke()) { Font = new System.Drawing.Font(contextMenu.Font, System.Drawing.FontStyle.Bold) });
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(_startupMenuItem);
        contextMenu.Items.Add(new ToolStripMenuItem("📁 Open Audio Folder (./audio)", null, (s, e) => OpenExplorer("./audio")));
        contextMenu.Items.Add(new ToolStripMenuItem("📁 Open Presets Folder (./presets)", null, (s, e) => OpenExplorer("./presets")));
        contextMenu.Items.Add(new ToolStripMenuItem("🔁 Rescan Audio Library", null, (s, e) => OnRescanRequested?.Invoke()));
        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("❌ Exit TTRPG SoundBoard", null, (s, e) => OnExitRequested?.Invoke()));

        _notifyIcon = new NotifyIcon
        {
            Icon = CreateAppIcon(),
            Text = "TTRPG SoundBoard - Running in System Tray",
            ContextMenuStrip = contextMenu,
            Visible = true
        };

        _notifyIcon.DoubleClick += (s, e) => OnOpenManagerRequested?.Invoke();
    }

    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, icon);
        }
        catch { }
    }

    private void OnStartupToggled(object? sender, EventArgs e)
    {
        bool newState = !_startupMenuItem.Checked;
        Helpers.StartupManager.SetStartup(newState);
        _startupMenuItem.Checked = Helpers.StartupManager.IsStartupEnabled;

        ShowNotification(
            "Windows Startup",
            _startupMenuItem.Checked ? "TTRPG SoundBoard will launch automatically when Windows starts." : "Auto-launch on Windows startup disabled."
        );
    }

    private static void OpenExplorer(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            if (!Directory.Exists(fullPath)) Directory.CreateDirectory(fullPath);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = fullPath,
                UseShellExecute = true
            });
        }
        catch { }
    }

    private static System.Drawing.Icon CreateAppIcon()
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(32, 32);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.Clear(System.Drawing.Color.Transparent);

                using var bgBrush = new System.Drawing.Drawing2D.LinearGradientBrush(
                    new System.Drawing.Rectangle(0, 0, 32, 32), 
                    System.Drawing.Color.FromArgb(255, 15, 17, 27), 
                    System.Drawing.Color.FromArgb(255, 45, 20, 35), 
                    45f);
                g.FillEllipse(bgBrush, 1, 1, 30, 30);

                using var borderPen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(255, 255, 71, 87), 1.5f);
                g.DrawEllipse(borderPen, 1, 1, 30, 30);

                using var faderPen = new System.Drawing.Pen(System.Drawing.Color.White, 2f);
                g.DrawLine(faderPen, 10, 8, 10, 24);
                g.DrawLine(faderPen, 16, 8, 16, 24);
                g.DrawLine(faderPen, 22, 8, 22, 24);

                using var knob1 = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 255, 71, 87));
                using var knob2 = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 255, 184, 0));
                using var knob3 = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, 46, 213, 115));

                g.FillRectangle(knob1, 7, 11, 6, 4);
                g.FillRectangle(knob2, 13, 17, 6, 4);
                g.FillRectangle(knob3, 19, 13, 6, 4);
            }

            IntPtr hIcon = bmp.GetHicon();
            return System.Drawing.Icon.FromHandle(hIcon);
        }
        catch
        {
            return System.Drawing.SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
