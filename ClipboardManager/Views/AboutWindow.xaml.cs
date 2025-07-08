using System.Windows;
using System.Windows.Input;
using System.Diagnostics;

namespace ClipboardManager.Views
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();
            Owner = System.Windows.Application.Current.MainWindow;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void GithubLink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://github.com/ManoilovaKaterina/ClipVault") { UseShellExecute = true });
        }

        private void FlaticonLink_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo("https://www.flaticon.com/free-icons/conclusion") { UseShellExecute = true });
        }
    }
}

