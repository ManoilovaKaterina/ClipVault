using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows;
using System.Reflection;
using System.IO;

namespace ClipboardManager.Utils
{
    public class TrayManager : IDisposable
    {
        private NotifyIcon _notifyIcon;
        private readonly Window _mainWindow;

        public TrayManager(Window mainWindow)
        {
            _mainWindow = mainWindow;
            InitializeTrayIcon();
        }

        private void InitializeTrayIcon()
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = GetApplicationIcon(),
                Text = "Clipboard Manager",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();

            var showMenuItem = new ToolStripMenuItem("Show", null, (s, e) => ShowWindow());
            var exitMenuItem = new ToolStripMenuItem("Exit", null, (s, e) => ExitApplication());

            contextMenu.Items.AddRange(new ToolStripItem[] { showMenuItem, exitMenuItem });

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (s, e) => ShowWindow();
        }

        private Icon GetApplicationIcon()
        {
            try
            {
                var iconUri = new Uri("pack://application:,,,/Assets/icon.ico", UriKind.Absolute);
                var streamInfo = System.Windows.Application.GetResourceStream(iconUri);
                if (streamInfo != null)
                {
                    return new Icon(streamInfo.Stream);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load tray icon: {ex.Message}");
            }

            return SystemIcons.Application;
        }

        private void ShowWindow()
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        private void ExitApplication()
        {
            App.IsShuttingDown = true;
            _mainWindow.Close();
        }

        public void ShowNotification(string title, string message)
        {
            _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
        }

        public void Dispose()
        {
            _notifyIcon?.Dispose();
        }
    }
}