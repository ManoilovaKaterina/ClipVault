using System;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using ClipboardManager.Models;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Media.Imaging;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Runtime.InteropServices;

namespace ClipboardManager.Services
{
    public class ClipboardService
    {
        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        private struct BITMAPFILEHEADER
        {
            public static readonly short BM = 0x4d42; // BM
            public short bfType;
            public int bfSize;
            public short bfReserved1;
            public short bfReserved2;
            public int bfOffBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private HwndSource _hwndSource;
        private readonly DatabaseService _databaseService;
        private ClipboardItem _lastClipboardItem;
        private readonly SemaphoreSlim _processingClipboard = new SemaphoreSlim(1, 1);
        private string _lastTextHash;
        private string _lastImageHash;
        private DateTime _lastProcessTime = DateTime.MinValue;
        private const int DEBOUNCE_DELAY_MS = 100; // Debounce rapid clipboard changes

        public event EventHandler<ClipboardItem> ClipboardChanged;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        public ClipboardService(DatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public void StartMonitoring(Window window)
        {
            var windowInteropHelper = new WindowInteropHelper(window);
            var handle = windowInteropHelper.Handle;

            _hwndSource = HwndSource.FromHwnd(handle);
            _hwndSource.AddHook(WndProc);

            AddClipboardFormatListener(handle);
        }

        public void StopMonitoring()
        {
            if (_hwndSource != null)
            {
                var handle = _hwndSource.Handle;
                RemoveClipboardFormatListener(handle);
                _hwndSource.RemoveHook(WndProc);
                _hwndSource = null;
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE)
            {
                // Use Task.Run to avoid blocking the UI thread
                _ = Task.Run(async () => await OnClipboardChanged());
            }

            return IntPtr.Zero;
        }

        private async Task OnClipboardChanged()
        {
            // Debounce rapid clipboard changes
            var currentTime = DateTime.Now;
            if ((currentTime - _lastProcessTime).TotalMilliseconds < DEBOUNCE_DELAY_MS)
            {
                return;
            }
            _lastProcessTime = currentTime;

            // Use semaphore to prevent multiple simultaneous clipboard processing
            if (!await _processingClipboard.WaitAsync(50)) // Short timeout to avoid hanging
            {
                return;
            }

            try
            {
                await ProcessClipboardContent();
            }
            finally
            {
                _processingClipboard.Release();
            }
        }
        private async Task ProcessClipboardContent()
        {
            try
            {
                ClipboardItem item = null;

                // Check clipboard content on UI thread with minimal time spent
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (System.Windows.Clipboard.ContainsImage())
                        {
                            var bitmapSource = System.Windows.Clipboard.GetImage();
                            item = CreateImageItem(bitmapSource);
                        }
                        else if (System.Windows.Clipboard.ContainsFileDropList())
                        {
                            var fileDropList = System.Windows.Clipboard.GetFileDropList();
                            item = CreateFileItem(fileDropList);
                        }
                        else if (System.Windows.Clipboard.ContainsText())
                        {
                            var text = System.Windows.Clipboard.GetText();
                            item = CreateTextItem(text);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error accessing clipboard: {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);

                if (item != null)
                {
                    // Process the item off the UI thread
                    await ProcessClipboardItem(item);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Clipboard processing error: {ex.Message}");
            }
        }
        private ClipboardItem CreateTextItem(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
                return null;

            // Quick hash check to avoid processing identical text
            var textHash = ComputeQuickHash(text);
            if (textHash == _lastTextHash)
                return null;

            _lastTextHash = textHash;

            return new ClipboardItem
            {
                Content = text,
                Timestamp = DateTime.Now,
                DataFormat = "Text",
                IsPinned = false
            };
        }

        public static class BinaryStructConverter
        {
            public static T FromByteArray<T>(byte[] bytes) where T : struct
            {
                IntPtr ptr = IntPtr.Zero;
                try
                {
                    int size = Marshal.SizeOf(typeof(T));
                    ptr = Marshal.AllocHGlobal(size);
                    Marshal.Copy(bytes, 0, ptr, size);
                    object obj = Marshal.PtrToStructure(ptr, typeof(T));
                    return (T)obj;
                }
                finally
                {
                    if (ptr != IntPtr.Zero)
                        Marshal.FreeHGlobal(ptr);
                }
            }

            public static byte[] ToByteArray<T>(T obj) where T : struct
            {
                IntPtr ptr = IntPtr.Zero;
                try
                {
                    int size = Marshal.SizeOf(typeof(T));
                    ptr = Marshal.AllocHGlobal(size);
                    Marshal.StructureToPtr(obj, ptr, true);
                    byte[] bytes = new byte[size];
                    Marshal.Copy(ptr, bytes, 0, size);
                    return bytes;
                }
                finally
                {
                    if (ptr != IntPtr.Zero)
                        Marshal.FreeHGlobal(ptr);
                }
            }
        }
        private ClipboardItem CreateImageItem(BitmapSource bitmapSource)
        {
            if (bitmapSource == null)
                return null;

            // Create a quick hash based on image dimensions and pixel format
            var imageInfo = $"{bitmapSource.PixelWidth}x{bitmapSource.PixelHeight}_{bitmapSource.Format}_{bitmapSource.DpiX}_{bitmapSource.DpiY}";
            var imageHash = ComputeQuickHash(imageInfo);

            if (imageHash == _lastImageHash)
                return null;

            _lastImageHash = imageHash;

            // Convert to byte array using the reliable DIB method
            var imageData = GetImageDataFromClipboard() ?? BitmapSourceToByteArray(bitmapSource);

            return new ClipboardItem
            {
                Content = $"Image ({bitmapSource.PixelWidth}x{bitmapSource.PixelHeight})",
                Timestamp = DateTime.Now,
                DataFormat = "Image",
                ImageData = imageData,
                ImageFormat = "PNG",
                IsPinned = false
            };
        }
        private byte[] GetImageDataFromClipboard()
        {
            try
            {
                // Try to get the DeviceIndependentBitmap format first (most reliable)
                var dataObject = System.Windows.Clipboard.GetDataObject();
                if (dataObject.GetDataPresent("DeviceIndependentBitmap"))
                {
                    var ms = dataObject.GetData("DeviceIndependentBitmap") as MemoryStream;
                    if (ms != null)
                    {
                        byte[] dibBuffer = new byte[ms.Length];
                        ms.Read(dibBuffer, 0, dibBuffer.Length);

                        var infoHeader = BinaryStructConverter.FromByteArray<BITMAPINFOHEADER>(dibBuffer);

                        int fileHeaderSize = Marshal.SizeOf(typeof(BITMAPFILEHEADER));
                        int infoHeaderSize = infoHeader.biSize;
                        int fileSize = fileHeaderSize + infoHeader.biSize + infoHeader.biSizeImage;

                        var fileHeader = new BITMAPFILEHEADER();
                        fileHeader.bfType = BITMAPFILEHEADER.BM;
                        fileHeader.bfSize = fileSize;
                        fileHeader.bfReserved1 = 0;
                        fileHeader.bfReserved2 = 0;
                        fileHeader.bfOffBits = fileHeaderSize + infoHeaderSize + infoHeader.biClrUsed * 4;

                        byte[] fileHeaderBytes = BinaryStructConverter.ToByteArray<BITMAPFILEHEADER>(fileHeader);

                        using var msBitmap = new MemoryStream();
                        msBitmap.Write(fileHeaderBytes, 0, fileHeaderSize);
                        msBitmap.Write(dibBuffer, 0, dibBuffer.Length);
                        msBitmap.Seek(0, SeekOrigin.Begin);

                        // Convert the complete bitmap to PNG for consistent storage
                        var bitmapFrame = BitmapFrame.Create(msBitmap);
                        return BitmapSourceToByteArray(bitmapFrame);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting image from clipboard DIB: {ex.Message}");
            }

            return null;
        }
        private ClipboardItem CreateFileItem(System.Collections.Specialized.StringCollection fileDropList)
        {
            if (fileDropList == null || fileDropList.Count == 0)
                return null;

            var filePaths = new System.Collections.Generic.List<string>();
            foreach (var filePath in fileDropList)
            {
                filePaths.Add(filePath);
            }

            return new ClipboardItem
            {
                Content = $"Files ({filePaths.Count})",
                Timestamp = DateTime.Now,
                DataFormat = "File",
                FilePaths = filePaths,
                IsPinned = false
            };
        }

        private async Task ProcessClipboardItem(ClipboardItem item)
        {
            if (IsDuplicateItem(item))
                return;

            // Check database for duplicates (this is the expensive operation, so do it off UI thread)
            var existingItem = await _databaseService.FindDuplicateItemAsync(item);

            if (existingItem != null)
            {
                // Update timestamp of existing item
                await _databaseService.UpdateTimestampAsync(existingItem.Id);
                _lastClipboardItem = existingItem;

                // Notify UI on the UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ClipboardChanged?.Invoke(this, existingItem);
                });
            }
            else
            {
                // Add new item to database
                await _databaseService.AddItemAsync(item);
                _lastClipboardItem = item;

                // Notify UI on the UI thread
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    ClipboardChanged?.Invoke(this, item);
                });
            }
        }

        private bool IsDuplicateItem(ClipboardItem newItem)
        {
            if (_lastClipboardItem == null)
                return false;

            // Check if it's the same type and content
            if (_lastClipboardItem.DataFormat != newItem.DataFormat)
                return false;

            switch (newItem.DataFormat)
            {
                case "Text":
                    return _lastClipboardItem.Content == newItem.Content;

                case "Image":
                    // For images, compare the content description (faster than byte comparison)
                    return _lastClipboardItem.Content == newItem.Content;

                case "File":
                    // For files, compare the file paths
                    return _lastClipboardItem.FilePaths != null &&
                           newItem.FilePaths != null &&
                           _lastClipboardItem.FilePaths.SequenceEqual(newItem.FilePaths);

                default:
                    return false;
            }
        }

        //private byte[] BitmapSourceToByteArray(BitmapSource bitmapSource)
        //{
        //    // Use PNG encoder instead of BMP for better compression and performance
        //    var encoder = new PngBitmapEncoder();
        //    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

        //    using var stream = new MemoryStream();
        //    encoder.Save(stream);
        //    return stream.ToArray();
        //}
        private byte[] BitmapSourceToByteArray(BitmapSource bitmapSource)
        {
            try
            {
                // Create a new BitmapSource with a consistent format
                var convertedBitmap = new FormatConvertedBitmap();
                convertedBitmap.BeginInit();
                convertedBitmap.Source = bitmapSource;
                convertedBitmap.DestinationFormat = System.Windows.Media.PixelFormats.Bgra32;
                convertedBitmap.EndInit();

                // Use PNG encoder for consistent format
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(convertedBitmap));

                using var stream = new MemoryStream();
                encoder.Save(stream);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting bitmap to byte array: {ex.Message}");

                // Fallback method - try with original bitmap
                try
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

                    using var stream = new MemoryStream();
                    encoder.Save(stream);
                    return stream.ToArray();
                }
                catch (Exception fallbackEx)
                {
                    Debug.WriteLine($"Fallback conversion also failed: {fallbackEx.Message}");
                    return null;
                }
            }
        }
        private string ComputeQuickHash(string input)
        {
            // Use a simple hash for quick comparison
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(bytes);
        }
    }
}