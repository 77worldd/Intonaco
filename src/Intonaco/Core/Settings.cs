using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intonaco.Core
{
    public sealed class AppSettings
    {
        public string? LastWallpaperPath { get; set; }
        public bool IsMuted { get; set; } = true;
        public double Volume { get; set; } = 0.0;
        public int TargetFps { get; set; } = 30;
        public bool PauseOnFullscreen { get; set; } = true;
        public bool StartWithWindows { get; set; } = false;
        public bool StartMinimized { get; set; } = false;
        public string Theme { get; set; } = "System"; // "Light", "Dark", "System"
        public bool AutoPlay { get; set; } = true;

        [JsonIgnore]
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Intonaco");

        [JsonIgnore]
        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    if (settings != null)
                    {
                        // Validate values
                        settings.Volume = Math.Clamp(settings.Volume, 0.0, 1.0);
                        settings.TargetFps = Math.Clamp(settings.TargetFps, 1, 60);
                        return settings;
                    }
                }
            }
            catch
            {
                // Return defaults on any error
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDir);
                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(SettingsFile, json);
            }
            catch
            {
                // Settings save failure is non-critical
            }
        }
    }
}
