using System;

namespace ClipboardManager.Models
{
    public class ClipboardItem
    {
        public int Id { get; set; }
        public string Content { get; set; }
        public DateTime Timestamp { get; set; }
        public string DataFormat { get; set; }
        public bool IsPinned { get; set; }
        public string Preview => Content?.Length > 100 ?
            Content.Substring(0, 100) + "..." : Content;
        public byte[] ImageData { get; set; }
        public string ImageFormat { get; set; }
        public bool IsImage => DataFormat == "Image";
        public List<string> FilePaths { get; set; }
        public bool IsFile => DataFormat == "File";
    }
}