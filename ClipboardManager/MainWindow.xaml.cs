using System;
using System.Windows;
using ClipboardManager.Services;
using ClipboardManager.ViewModels;
using ClipboardManager.Utils;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using ClipboardManager.Views;

namespace ClipboardManager
{
    public partial class MainWindow : Window
    {
        private readonly DatabaseService _databaseService;
        private readonly ClipboardService _clipboardService;
        private readonly MainViewModel _viewModel;
        private TrayManager _trayManager;

        public MainWindow()
        {
            InitializeComponent();
            _databaseService = new DatabaseService();
            _clipboardService = new ClipboardService(_databaseService);
            _viewModel = new MainViewModel(_databaseService);
            DataContext = _viewModel;

            // Initialize tray manager
            _trayManager = new TrayManager(this);

            // Subscribe to clipboard changes
            _clipboardService.ClipboardChanged += OnClipboardChanged;
            Loaded += MainWindow_Loaded;
            Closing += MainWindow_Closing;

            // Set window to appear at bottom right corner
            SetWindowPosition();
        }

        private void SetWindowPosition()
        {
            // Get screen working area (excludes taskbar)
            var workingArea = SystemParameters.WorkArea;

            // Position window at bottom right corner with some margin
            Left = workingArea.Right - Width - 20;  // 20px margin from right
            Top = workingArea.Bottom - Height - 20;  // 20px margin from bottom
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Start clipboard monitoring
            _clipboardService.StartMonitoring(this);

            // Focus search box
            SearchTextBox.Focus();
        }

        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!App.IsShuttingDown)
            {
                // If the user is closing the window, minimize to tray instead
                e.Cancel = true;
                this.Hide();
                _trayManager?.ShowNotification("Clipboard Manager", "Application minimized to tray. Right-click tray icon to exit.");
            }
            else
            {
                // Stop clipboard monitoring
                _clipboardService.StopMonitoring();
                // Dispose tray manager
                _trayManager?.Dispose();
                // Delete unpinned items
                _databaseService.DeleteUnpinnedItemsAsync().Wait();
            }
        }

        private void OnClipboardChanged(object sender, Models.ClipboardItem e)
        {
            Dispatcher.Invoke(() =>
            {
                _viewModel.AddNewItem(e);
                _trayManager?.ShowNotification("Clipboard Manager", "New item added to clipboard history");
            });
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        // Custom title bar event handlers
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double-click to maximize/restore (optional)
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                // Single-click to drag
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AboutButton_Click(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new AboutWindow
            {
                Owner = this
            };
            aboutWindow.ShowDialog();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow
            {
                Owner = this
            };
            settingsWindow.ShowDialog();
        }

        private void ClipboardListView_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            var scrollViewer = FindVisualChild<ScrollViewer>(ClipboardListView);
            if (scrollViewer != null)
            {
                double scrollSpeed = 0.5; 
                double offset = scrollViewer.VerticalOffset - (e.Delta * scrollSpeed / 6);
                scrollViewer.ScrollToVerticalOffset(offset);
                e.Handled = true;
            }
        }

        private static T FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child != null && child is T)
                {
                    return (T)child;
                }
                else
                {
                    T childOfChild = FindVisualChild<T>(child);
                    if (childOfChild != null)
                    {
                        return childOfChild;
                    }
                }
            }
            return null;
        }
    }
}