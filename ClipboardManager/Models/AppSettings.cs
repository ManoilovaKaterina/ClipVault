namespace ClipboardManager.Models
{
    public class AppSettings
    {
        public int MaxHistoryItems { get; set; } = 100;
        public bool StartWithWindows { get; set; } = true;
        public string HotKey { get; set; } = "Ctrl+Shift+V";
        public bool ShowNotifications { get; set; } = true;
    }
}