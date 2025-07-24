using System.Windows;
using Microsoft.Win32;

namespace ClipboardManager
{
    public partial class App : System.Windows.Application
    {
        public static bool IsShuttingDown { get; set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            key?.SetValue("ClipVault", $"\"{Environment.ProcessPath}\"");

            // Ensure only one instance runs
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var runningProcesses = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);

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