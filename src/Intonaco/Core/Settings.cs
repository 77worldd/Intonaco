using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Intonaco.Core
{
    public sealed class AppSettings
    {
        internal const int MaxRecentWallpapers = 6;

        public string? LastWallpaperPath { get; set; }
        public List<string> RecentWallpaperPaths { get; set; } = new List<string>();
        public bool IsMuted { get; set; } = true;
        public double Volume { get; set; } = 0.0;
        public int TargetFps { get; set; } = 30;
        public string WallpaperFit { get; set; } = "Fill"; // "Fill", "Fit", "Stretch"
        public double PlaybackSpeed { get; set; } = 1.0;
        public bool LoopPlayback { get; set; } = true;
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

        internal static string NormalizeFit(string? fit)
        {
            if (string.Equals(fit, "Fit", StringComparison.OrdinalIgnoreCase))
                return "Fit";
            if (string.Equals(fit, "Stretch", StringComparison.OrdinalIgnoreCase))
                return "Stretch";
            return "Fill";
        }

        internal void Normalize()
        {
            Volume = double.IsNaN(Volume) || double.IsInfinity(Volume)
                ? 0.0
                : Math.Clamp(Volume, 0.0, 1.0);
            TargetFps = Math.Clamp(TargetFps, 1, 60);
            PlaybackSpeed = double.IsNaN(PlaybackSpeed) || double.IsInfinity(PlaybackSpeed)
                ? 1.0
                : Math.Clamp(PlaybackSpeed, 0.25, 2.0);
            WallpaperFit = NormalizeFit(WallpaperFit);
            Theme = Theme switch
            {
                "Light" => "Light",
                "Dark" => "Dark",
                _ => "System"
            };

            RecentWallpaperPaths ??= new List<string>();
            RecentWallpaperPaths = RecentWallpaperPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(MaxRecentWallpapers)
                .ToList();
        }

        public void RememberWallpaper(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return;

            Normalize();
            RecentWallpaperPaths.RemoveAll(path =>
                string.Equals(path, filePath, StringComparison.OrdinalIgnoreCase));
            RecentWallpaperPaths.Insert(0, filePath);
            if (RecentWallpaperPaths.Count > MaxRecentWallpapers)
            {
                RecentWallpaperPaths.RemoveRange(
                    MaxRecentWallpapers,
                    RecentWallpaperPaths.Count - MaxRecentWallpapers);
            }
            LastWallpaperPath = filePath;
        }

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
                        settings.Normalize();
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
                Normalize();
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
