using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SynkrFox.Core
{
    public class AppSettings
    {
        public List<string> SourceFolders { get; set; } = new();
        public string BackupPath { get; set; } = "";
        public bool StartWithPC { get; set; } = true;
    }

    public static class ConfigManager
    {
        private static string ConfigPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SynkrFox", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    string json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch { }
            return new AppSettings();
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                string? configPath = ConfigPath;
                if (string.IsNullOrEmpty(configPath)) return;

                string? dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configPath, json);
            }
            catch { }
        }
    }
}
