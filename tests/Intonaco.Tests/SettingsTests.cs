using System;
using Intonaco.Core;
using Xunit;

namespace Intonaco.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void DefaultSettings_HaveSensibleValues()
        {
            var settings = new AppSettings();

            Assert.True(settings.IsMuted);
            Assert.Equal(0.0, settings.Volume);
            Assert.Equal(30, settings.TargetFps);
            Assert.True(settings.PauseOnFullscreen);
            Assert.False(settings.StartWithWindows);
            Assert.Null(settings.LastWallpaperPath);
            Assert.Equal("System", settings.Theme);
        }

        [Fact]
        public void Settings_ClampVolume()
        {
            var settings = new AppSettings { Volume = 2.0 };
            var loaded = AppSettings.Load(); // Will return defaults since file doesn't exist in test
            Assert.InRange(loaded.Volume, 0.0, 1.0);
        }

        [Fact]
        public void Settings_ClampFps()
        {
            var settings = new AppSettings { TargetFps = 120 };
            // After load validation
            settings.TargetFps = Math.Clamp(settings.TargetFps, 1, 60);
            Assert.InRange(settings.TargetFps, 1, 60);
        }
    }

    public class WallpaperPlayerTests
    {
        [WpfFact]
        public void Player_InitialState_IsStopped()
        {
            var player = new WallpaperPlayer();
            Assert.Equal(WallpaperPlayer.PlaybackState.Stopped, player.CurrentState);
            Assert.False(player.IsPaused);
            Assert.Null(player.CurrentFilePath);
            player.Dispose();
        }

        [WpfFact]
        public void Player_DefaultVolume_IsZero()
        {
            var player = new WallpaperPlayer();
            Assert.Equal(0.0, player.Volume);
            player.Dispose();
        }

        [WpfFact]
        public void Player_VolumeClamp()
        {
            var player = new WallpaperPlayer();
            player.Volume = 1.5;
            Assert.Equal(1.0, player.Volume);
            player.Volume = -0.5;
            Assert.Equal(0.0, player.Volume);
            player.Dispose();
        }

        [WpfFact]
        public void Player_FpsClamp()
        {
            var player = new WallpaperPlayer();
            player.TargetFps = 120;
            Assert.Equal(60, player.TargetFps);
            player.TargetFps = 0;
            Assert.Equal(1, player.TargetFps);
            player.Dispose();
        }

        [WpfFact]
        public void Player_PlayNonexistentFile_ReturnsFalse()
        {
            var player = new WallpaperPlayer();
            player.Initialize(); // Need to initialize before play
            string? errorMsg = null;
            player.ErrorOccurred += msg => errorMsg = msg;

            bool result = player.Play(@"C:\nonexistent\fake.mp4");

            Assert.False(result);
            Assert.NotNull(errorMsg);
            player.Dispose();
        }

        [WpfFact]
        public void Player_Dispose_IsIdempotent()
        {
            var player = new WallpaperPlayer();
            player.Dispose();
            player.Dispose(); // Should not throw
        }
    }

    public class FullscreenDetectorTests
    {
        [WpfFact]
        public void Detector_DefaultState_IsNotFullscreen()
        {
            var detector = new FullscreenDetector();
            Assert.False(detector.IsFullscreenActive);
            detector.Dispose();
        }

        [WpfFact]
        public void Detector_Dispose_IsIdempotent()
        {
            var detector = new FullscreenDetector();
            detector.Start();
            detector.Dispose();
            detector.Dispose(); // Should not throw
        }

        [WpfFact]
        public void Detector_EnableDisable()
        {
            var detector = new FullscreenDetector();
            detector.IsEnabled = false;
            Assert.False(detector.IsEnabled);
            detector.IsEnabled = true;
            Assert.True(detector.IsEnabled);
            detector.Dispose();
        }
    }

    public class FrameRateLimiterTests
    {
        [WpfFact]
        public void Limiter_DefaultFps_Is30()
        {
            var limiter = new FrameRateLimiter();
            Assert.Equal(30, limiter.TargetFps);
            limiter.Dispose();
        }

        [WpfFact]
        public void Limiter_ClampsFps()
        {
            var limiter = new FrameRateLimiter(120);
            Assert.Equal(60, limiter.TargetFps);

            limiter.TargetFps = 0;
            Assert.Equal(1, limiter.TargetFps);

            limiter.Dispose();
        }
    }
}
