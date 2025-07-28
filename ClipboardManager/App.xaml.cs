using ClipboardManager.Services;
using Microsoft.Win32;
using System.Windows;

namespace ClipboardManager
{
    public partial class App : System.Windows.Application
    {
        public static bool IsShuttingDown { get; set; } = false;
        public static bool StartedAtStartup { get; private set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            StartedAtStartup = e.Args.Length > 0 && e.Args[0] == "--startup";
            base.OnStartup(e);

            // Ensure only one instance runs
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var runningProcesses = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);
            
            ThemeManager.Initialize();
            
            if (runningProcesses.Length > 1)
            {
                System.Windows.MessageBox.Show("Clipboard Manager is already running!", "Already Running",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }
    }
}