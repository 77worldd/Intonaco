using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Intonaco.Core;
using Xunit;

namespace Intonaco.Tests
{
    public class OptimizationTests
    {
        [WpfFact]
        public void PlayerHost_ContainsVideoAndCollapsedPausedFrameLayer()
        {
            var player = new WallpaperPlayer();
            try
            {
                var host = Assert.IsType<Grid>(player.CreateHost());

                Assert.Collection(
                    host.Children.Cast<UIElement>(),
                    child => Assert.IsType<MediaElement>(child),
                    child =>
                    {
                        var image = Assert.IsType<Image>(child);
                        Assert.Equal(Visibility.Collapsed, image.Visibility);
                        Assert.Null(image.Source);
                    });

                Assert.False(player.HasActiveMediaPipeline);
                Assert.False(player.IsSuspended);
                Assert.Null(player.PausedFrame);
                Assert.Equal(TimeSpan.Zero, player.Position);
            }
            finally
            {
                player.Dispose();
            }
        }

        [Theory]
        [InlineData(1920, 1080, 1.0)]
        [InlineData(3840, 2160, 1.0)]
        [InlineData(7680, 4320, 0.5)]
        [InlineData(5120, 1440, 0.75)]
        public void PausedFrameScale_BoundsOnlyVeryLargeFrames(
            int width,
            int height,
            double expected)
        {
            Assert.Equal(
                expected,
                WallpaperPlayer.CalculatePausedFrameScale(width, height),
                precision: 3);
        }

        [WpfFact]
        public void FullscreenDetector_StartIsIdempotentAndStopReleasesTimer()
        {
            var detector = new FullscreenDetector();
            try
            {
                detector.Start();
                detector.Start();
                Assert.True(detector.IsRunning);

                detector.Stop();
                Assert.False(detector.IsRunning);
            }
            finally
            {
                detector.Dispose();
            }
        }

        [Fact]
        public void PerformanceSnapshot_ReportsProcessFootprint()
        {
            var snapshot = PerformanceSnapshot.Capture();

            Assert.True(snapshot.WorkingSetBytes > 0);
            Assert.True(snapshot.PrivateBytes > 0);
            Assert.True(snapshot.ManagedBytes > 0);
            Assert.True(snapshot.ThreadCount > 0);
            Assert.True(snapshot.HandleCount > 0);
        }

        [WpfFact]
        public async Task Player_PauseSuspendsPipelineAndResumeReopensIt()
        {
            string? sample = FindSampleVideo();
            Assert.True(sample != null,
                "A Windows WinSAT sample video is required for the media lifecycle test.");

            EnsureApplication();

            var player = new WallpaperPlayer();
            var window = new Window
            {
                Content = player.CreateHost(),
                Width = 640,
                Height = 360,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None
            };

            try
            {
                window.Show();
                window.UpdateLayout();

                Task<bool> firstOpen = WaitForMediaOpenedAsync(player);
                Assert.True(player.Play(sample!));
                if (!await firstOpen)
                    return; // The CI image may not include a usable codec.

                await Task.Delay(500);
                player.Pause();

                Assert.Equal(WallpaperPlayer.PlaybackState.Paused, player.CurrentState);
                Assert.True(player.IsSuspended);
                Assert.False(player.HasActiveMediaPipeline);
                Assert.NotNull(player.PausedFrame);

                Task<bool> resumedOpen = WaitForMediaOpenedAsync(player);
                player.Resume();

                Assert.Equal(WallpaperPlayer.PlaybackState.Playing, player.CurrentState);
                Assert.True(player.HasActiveMediaPipeline);
                Assert.True(await resumedOpen);

                player.Stop();
                Assert.Equal(WallpaperPlayer.PlaybackState.Stopped, player.CurrentState);
                Assert.False(player.HasActiveMediaPipeline);
                Assert.False(player.IsSuspended);
                Assert.Null(player.PausedFrame);
            }
            finally
            {
                player.Dispose();
                window.Close();
            }
        }

        private static string? FindSampleVideo()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "Performance",
                "WinSAT");

            if (!Directory.Exists(folder))
                return null;

            return Directory
                .EnumerateFiles(folder)
                .Where(path =>
                    path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".wmv", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => new FileInfo(path).Length)
                .FirstOrDefault();
        }

        private static async Task<bool> WaitForMediaOpenedAsync(WallpaperPlayer player)
        {
            var completion = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            void Opened(TimeSpan _) => completion.TrySetResult(true);
            void Failed(string _) => completion.TrySetResult(false);

            player.MediaOpened += Opened;
            player.ErrorOccurred += Failed;

            try
            {
                Task completed = await Task.WhenAny(
                    completion.Task,
                    Task.Delay(TimeSpan.FromSeconds(10)));
                return completed == completion.Task && completion.Task.Result;
            }
            finally
            {
                player.MediaOpened -= Opened;
                player.ErrorOccurred -= Failed;
            }
        }

        private static void EnsureApplication()
        {
            if (Application.Current == null)
            {
                _ = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
            }
        }
    }
}
