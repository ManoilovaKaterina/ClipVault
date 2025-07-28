using ClipboardManager.Services;
using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Input;

namespace ClipboardManager.Views
{
    /// <summary>
    /// Interaction logic for SettingsWindow.xaml
    /// </summary>
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Set initial toggle states
            IsDarkMode = ThemeManager.CurrentTheme == Theme.Dark;
            IsStartupEnabled = CheckStartupEnabled();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        #region Dark Mode Property
        public bool IsDarkMode
        {
            get { return (bool)GetValue(IsDarkModeProperty); }
            set { SetValue(IsDarkModeProperty, value); }
        }

        public static readonly DependencyProperty IsDarkModeProperty =
            DependencyProperty.Register("IsDarkMode", typeof(bool), typeof(SettingsWindow),
                new PropertyMetadata(true));
        #endregion

        #region Startup Property
        public bool IsStartupEnabled
        {
            get { return (bool)GetValue(IsStartupEnabledProperty); }
            set { SetValue(IsStartupEnabledProperty, value); }
        }

        public static readonly DependencyProperty IsStartupEnabledProperty =
            DependencyProperty.Register("IsStartupEnabled", typeof(bool), typeof(SettingsWindow),
                new PropertyMetadata(false));
        #endregion

        #region Dark Mode Toggle Handlers
        private void DarkModeToggle_Checked(object sender, RoutedEventArgs e)
        {
            ThemeManager.CurrentTheme = Theme.Dark;
        }

        private void DarkModeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            ThemeManager.CurrentTheme = Theme.Light;
        }
        #endregion

        #region Startup Toggle Handlers
        private void StartupToggle_Checked(object sender, RoutedEventArgs e)
        {
            SetStartupEnabled(true);
        }

        private void StartupToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            SetStartupEnabled(false);
        }
        #endregion

        #region Startup Registry Methods
        private bool CheckStartupEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false);
                return key?.GetValue("ClipVault") != null;
            }
            catch
            {
                return false;
            }
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
        #endregion
    }
}