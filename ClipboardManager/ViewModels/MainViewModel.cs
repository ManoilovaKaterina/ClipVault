using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ClipboardManager.Models;
using ClipboardManager.Services;
using ClipboardManager.Views;

namespace ClipboardManager.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly DatabaseService _databaseService;
        private string _searchText = string.Empty;
        private ClipboardItem _selectedItem;
        private int _selectedFilterIndex = 0; // 0: All, 1: Text, 2: Images, 3: Files

        public ObservableCollection<ClipboardItem> ClipboardItems { get; set; }
        public ObservableCollection<ClipboardItem> FilteredItems { get; set; }

        public ICommand CopyItemCommand { get; }
        public ICommand DeleteItemCommand { get; }
        public ICommand TogglePinCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand SearchCommand { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged();
                FilterItems();
            }
        }

        public ClipboardItem SelectedItem
        {
            get => _selectedItem;
            set
            {
                _selectedItem = value;
                OnPropertyChanged();
            }
        }

        public int SelectedFilterIndex
        {
            get => _selectedFilterIndex;
            set
            {
                _selectedFilterIndex = value;
                OnPropertyChanged();
                FilterItems();
            }
        }

        public MainViewModel(DatabaseService databaseService)
        {
            _databaseService = databaseService;
            ClipboardItems = new ObservableCollection<ClipboardItem>();
            FilteredItems = new ObservableCollection<ClipboardItem>();

            CopyItemCommand = new RelayCommand<ClipboardItem>(CopyItem);
            DeleteItemCommand = new RelayCommand<ClipboardItem>(DeleteItem);
            TogglePinCommand = new RelayCommand<ClipboardItem>(TogglePin);
            ClearHistoryCommand = new RelayCommand(ClearHistory);
            SearchCommand = new RelayCommand(FilterItems);

            _ = LoadItems();
        }

        private async Task LoadItems()
        {
            var items = await _databaseService.GetAllItemsAsync();
            ClipboardItems.Clear();

            foreach (var item in items)
            {
                ClipboardItems.Add(item);
            }
            FilterItems();
        }

        private void FilterItems()
        {
            FilteredItems.Clear();
            var itemsToFilter = ClipboardItems.AsEnumerable();

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                itemsToFilter = itemsToFilter.Where(item =>
                    (item.Content != null && item.Content.Contains(SearchText, StringComparison.OrdinalIgnoreCase)) ||
                    (item.FilePaths != null && item.FilePaths.Any(path => path.Contains(SearchText, StringComparison.OrdinalIgnoreCase)))
                );
            }

            // Apply type filter
            switch (SelectedFilterIndex)
            {
                case 1: // Text
                    itemsToFilter = itemsToFilter.Where(item => item.DataFormat == "Text");
                    break;
                case 2: // Images
                    itemsToFilter = itemsToFilter.Where(item => item.DataFormat == "Image");
                    break;
                case 3: // Files
                    itemsToFilter = itemsToFilter.Where(item => item.DataFormat == "File");
                    break;
                default: // All (case 0)
                    break;
            }

            foreach (var item in itemsToFilter.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.Timestamp))
            {
                FilteredItems.Add(item);
            }
        }

        private void CopyItem(ClipboardItem item)
        {
            if (item != null)
            {
                if (item.IsImage && item.ImageData != null)
                {
                    var bitmapImage = ByteArrayToBitmapImage(item.ImageData);
                    System.Windows.Clipboard.SetImage(bitmapImage);
                }
                else if (item.IsFile && item.FilePaths != null)
                {
                    var fileDropList = new System.Collections.Specialized.StringCollection();
                    foreach (var path in item.FilePaths)
                    {
                        fileDropList.Add(path);
                    }
                    System.Windows.Clipboard.SetFileDropList(fileDropList);
                }
                else
                {
                    System.Windows.Clipboard.SetText(item.Content);
                }
            }
        }

        private BitmapImage ByteArrayToBitmapImage(byte[] imageData)
        {
            var bitmapImage = new BitmapImage();
            using var stream = new MemoryStream(imageData);

            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = stream;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            return bitmapImage;
        }

        private async void DeleteItem(ClipboardItem item)
        {
            if (item != null)
            {
                await _databaseService.DeleteItemAsync(item.Id);
                ClipboardItems.Remove(item);
                FilteredItems.Remove(item);
            }
        }

        private async void TogglePin(ClipboardItem item)
        {
            if (item != null)
            {
                await _databaseService.TogglePinAsync(item.Id);
                item.IsPinned = !item.IsPinned;
                // Refresh the view to update grouping
                System.Windows.Data.CollectionViewSource.GetDefaultView(FilteredItems).Refresh();
                await LoadItems(); // Refresh to get proper ordering
            }
        }

        private async void ClearHistory()
        {
            var dialog = new ConfirmDialog("Are you sure you want to clear clipboard history?");
            dialog.ShowDialog();

            if (dialog.Result)
            {
                await _databaseService.ClearHistoryAsync();
                await LoadItems();
            }
        }

        public async void AddNewItem(ClipboardItem item)
        {
            // For file items, always add as new to avoid content string conflicts
            if (item.DataFormat == "File")
            {
                ClipboardItems.Insert(0, item);
            }
            else
            {
                // Check if item already exists for non-file types
                var existing = ClipboardItems.FirstOrDefault(x => x.Content == item.Content);
                if (existing != null)
                {
                    // Update timestamp of existing item
                    existing.Timestamp = DateTime.Now;
                    await LoadItems();
                    return;
                }

                ClipboardItems.Insert(0, item);
            }

            FilterItems();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Simple RelayCommand implementation
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute();

        public void Execute(object parameter) => _execute();

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }

    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute((T)parameter);

        public void Execute(object parameter) => _execute((T)parameter);

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }
    }
}