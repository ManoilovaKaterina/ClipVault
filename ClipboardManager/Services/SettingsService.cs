using System;
using System.IO;
using System.Text.Json;

namespace ClipboardManager.Services
{
    public class SettingsService
    {
        private const string SettingsFileName = "settings.json";
        private readonly string _settingsPath;

        public SettingsService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "ClipVault");
            Directory.CreateDirectory(appFolder);
            _settingsPath = Path.Combine(appFolder, SettingsFileName);
        }

        public bool IsFirstRun()
        {
            return !File.Exists(_settingsPath);
        }

        public void MarkFirstRunComplete()
        {
            var settings = new { FirstRunCompleted = true };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
    }
}