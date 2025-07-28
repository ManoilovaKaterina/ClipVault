using Microsoft.Win32;
using System;
using System.Windows;

namespace ClipboardManager.Views
{
    public partial class FirstRunDialog : Window
    {
        public bool EnableStartup { get; private set; } = false;

        public FirstRunDialog()
        {
            InitializeComponent();
            Owner = System.Windows.Application.Current.MainWindow;
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            EnableStartup = true;
            SetStartupEnabled(true);
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            EnableStartup = false;
            Close();
        }

        private void SetStartupEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                if (key != null)
                {
                    if (enabled)
                    {
                        key.SetValue("ClipVault", $"\"{Environment.ProcessPath}\" --startup");
                    }
                    else
                    {
                        try
                        {
                            key.DeleteValue("ClipVault", false);
                        }
                        catch (ArgumentException)
                        {
                            // Value doesn't exist, which is fine
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Failed to update startup setting: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}