using System;
using System.Windows;

namespace ClipboardManager.Services
{
    public enum Theme
    {
        Light,
        Dark
    }

    public static class ThemeManager
    {
        private static Theme _currentTheme = Theme.Dark; // Default to Dark

        public static Theme CurrentTheme
        {
            get => _currentTheme;
            set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    ApplyTheme(value);
                }
            }
        }

        public static void Initialize()
        {
            // Apply default dark theme on startup
            ApplyTheme(Theme.Dark);
        }

        private static void ApplyTheme(Theme theme)
        {
            var app = System.Windows.Application.Current;
            if (app?.Resources == null) return;

            // Clear existing theme resources
            var resourcesToRemove = new string[] { "DarkTheme.xaml", "LightTheme.xaml" };

            for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var dict = app.Resources.MergedDictionaries[i];
                if (dict.Source != null)
                {
                    string fileName = System.IO.Path.GetFileName(dict.Source.OriginalString);
                    if (Array.Exists(resourcesToRemove, x => x == fileName))
                    {
                        app.Resources.MergedDictionaries.RemoveAt(i);
                    }
                }
            }

            // Add the new theme
            var themeUri = new Uri($"Themes/{theme}Theme.xaml", UriKind.Relative);
            var themeDict = new ResourceDictionary { Source = themeUri };

            // Insert at the beginning so theme colors take precedence
            app.Resources.MergedDictionaries.Insert(0, themeDict);
        }
    }
}