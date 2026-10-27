using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Intonaco.Core;
using Xunit;

namespace Intonaco.Tests
{
    /// <summary>
    /// Regression tests for the black-border defect that appeared when the
    /// user manually paused the wallpaper.
    ///
    /// Root cause: Pause() did far more than stop animation updates. It
    /// photographed the WPF MediaElement into a RenderTargetBitmap, then
    /// called Stop()/Close(), set Source = null and collapsed the element,
    /// replacing the live video surface with a separate Image. MediaElement
    /// video is presented by Media Foundation into a native surface that is
    /// not part of WPF's managed visual tree, so that "photograph" was not a
    /// faithful copy of the desktop (black bands/blocks), and closing the
    /// source removed the only surface holding the real frame. v1.0.0 simply
    /// paused the media clock and never had black borders.
    ///
    /// These tests pin the invariant: pausing stops the clock and must not
    /// touch the rendering surface, and the host must stay full-bleed.
    /// </summary>
    public class PauseRenderingTests
    {
        [WpfFact]
        public void Host_KeepsOneLiveFullBleedVideoSurface()
        {
            var player = new WallpaperPlayer();
            try
            {
                var host = Assert.IsType<Grid>(player.CreateHost());
                var media = Assert.IsType<MediaElement>(host.Children[0]);

                Assert.Equal(Stretch.UniformToFill, media.Stretch);
                Assert.Equal(Visibility.Visible, media.Visibility);
                Assert.Equal(new Thickness(0), media.Margin);
                Assert.Equal(HorizontalAlignment.Stretch, media.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Stretch, media.VerticalAlignment);

                Assert.Equal(new Thickness(0), host.Margin);
                Assert.Equal(HorizontalAlignment.Stretch, host.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Stretch, host.VerticalAlignment);
                Assert.True(host.ClipToBounds);

                // Any secondary layer must stay hidden: the live MediaElement
                // is the only thing allowed to render the wallpaper.
                var pausedLayer = Assert.IsType<Image>(host.Children[1]);
                Assert.Equal(Visibility.Collapsed, pausedLayer.Visibility);
                Assert.Null(pausedLayer.Source);
                Assert.Null(player.PausedFrame);
                Assert.False(player.IsSuspended);
            }
            finally
            {
                player.Dispose();
            }
        }

        [Fact]
        public void Pause_OnlyStopsTheMediaClock_NeverTearsDownTheSurface()
        {
            string source = ReadSource("src/Intonaco/Core/WallpaperPlayer.cs");
            string body = MethodBody(source, "public void Pause()");

            Assert.Contains("_mediaElement.Pause()", body);
            Assert.Contains("SetState(PlaybackState.Paused)", body);

            // The operations that produced black borders/blocks. None of them
            // may come back on the manual-pause path.
            Assert.DoesNotContain("SuspendMediaPipeline()", body);
            Assert.DoesNotContain("CaptureCurrentFrame()", body);
            Assert.DoesNotContain("SetPausedFrame(", body);
            Assert.DoesNotContain("ReleaseMediaPipeline()", body);
            Assert.DoesNotContain("Visibility.Collapsed", body);
            Assert.DoesNotContain(".Close()", body);
            Assert.DoesNotContain("Source = null", body);
        }

        [Fact]
        public void Resume_NeverReopensOrSeeksTheSource()
        {
            string source = ReadSource("src/Intonaco/Core/WallpaperPlayer.cs");
            string body = MethodBody(source, "public void Resume()");

            Assert.Contains("_mediaElement.Play()", body);
            Assert.DoesNotContain("Source = _currentSource", body);
            Assert.DoesNotContain("HidePausedFrameAfterRender()", body);
            Assert.DoesNotContain("new Uri(", body);
        }

        [Fact]
        public void Preview_WhilePaused_KeepsItsOwnMediaElementVisible()
        {
            string source = ReadSource("src/Intonaco/Views/MainWindow.xaml.cs");
            string body = MethodBody(source, "private void ShowPausedPreview()");

            Assert.Contains("PreviewMedia.Pause()", body);
            Assert.DoesNotContain("ReleasePreviewPipeline()", body);
            Assert.DoesNotContain("CaptureFrame(", body);
            Assert.Contains("PreviewMedia.Visibility = Visibility.Visible", body);
        }

        /// <summary>
        /// Finds a repository source file from the test host's output folder.
        /// </summary>
        private static string ReadSource(string relativePath)
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null)
            {
                string candidate = Path.Combine(directory.FullName, relativePath);
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);

                // Also handle a solution-level test working directory.
                candidate = Path.Combine(directory.FullName, "Intonaco", relativePath);
                if (File.Exists(candidate))
                    return File.ReadAllText(candidate);

                directory = directory.Parent;
            }

            throw new FileNotFoundException(
                $"Could not locate {relativePath} from {AppContext.BaseDirectory}.");
        }

        private static string MethodBody(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing {signature} in source.");

            int open = source.IndexOf('{', start);
            Assert.True(open > start, $"Missing opening brace for {signature}.");

            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0)
                    return source.Substring(open, i - open + 1);
            }

            throw new InvalidOperationException($"Unbalanced braces for {signature}.");
        }
    }
}
