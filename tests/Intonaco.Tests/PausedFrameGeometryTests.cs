using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Intonaco.Core;
using Xunit;

namespace Intonaco.Tests
{
    /// <summary>
    /// Regression tests for the "black bars appear when the wallpaper is
    /// paused" defect.
    ///
    /// The bars were never a container-geometry problem. The paused wallpaper
    /// is a <see cref="RenderTargetBitmap"/> photograph of the MediaElement, so
    /// any letterbox it was showing became real black pixels inside the bitmap;
    /// the static layer then displayed that bitmap with <c>Stretch.Fill</c>,
    /// which happily scaled the baked-in bars up to full screen. No amount of
    /// margin/padding/alignment hardening on the window or host grid can remove
    /// bars that are part of the image.
    ///
    /// These tests pin the two properties that make bars impossible: the frozen
    /// frame is cropped to the desktop host's aspect ratio, and the layer that
    /// shows it covers rather than letterboxes.
    /// </summary>
    public class PausedFrameGeometryTests
    {
        private const uint Black = 0xFF000000;
        private const uint White = 0xFFFFFFFF;

        // 16:9, the overwhelmingly common desktop aspect.
        private const double DesktopAspect = 16.0 / 9.0;

        [WpfFact]
        public void PausedImageLayer_CoversTheHostInsteadOfLetterboxing()
        {
            var player = new WallpaperPlayer();
            try
            {
                var host = Assert.IsType<Grid>(player.CreateHost());
                var pausedImage = Assert.IsType<Image>(host.Children[1]);

                // Stretch.Fill was the original defect: it stretches whatever
                // aspect the capture happened to have, so a mismatched capture
                // shows bars. Stretch.Uniform letterboxes by definition. Only
                // UniformToFill guarantees every pixel of the host is covered.
                Assert.Equal(Stretch.UniformToFill, pausedImage.Stretch);
                Assert.Equal(new Thickness(0), pausedImage.Margin);
                Assert.Equal(HorizontalAlignment.Stretch, pausedImage.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Stretch, pausedImage.VerticalAlignment);
                Assert.Equal(Visibility.Collapsed, pausedImage.Visibility);
            }
            finally
            {
                player.Dispose();
            }
        }

        [WpfFact]
        public void HostAndMedia_FillTheDesktopLayerWithNoInset()
        {
            var player = new WallpaperPlayer();
            try
            {
                var host = Assert.IsType<Grid>(player.CreateHost());
                var media = Assert.IsType<MediaElement>(host.Children[0]);

                // The host is the authoritative desktop box: its aspect ratio is
                // what the paused frame is cropped to, so it must be inset by
                // nothing, or the crop target is wrong.
                Assert.Equal(new Thickness(0), host.Margin);
                Assert.Equal(HorizontalAlignment.Stretch, host.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Stretch, host.VerticalAlignment);
                Assert.True(host.ClipToBounds);

                Assert.Equal(new Thickness(0), media.Margin);
                Assert.Equal(HorizontalAlignment.Stretch, media.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Stretch, media.VerticalAlignment);
            }
            finally
            {
                player.Dispose();
            }
        }

        [WpfFact]
        public void CentreCrop_TrimsTheSidesWhenTheFrameIsWiderThanTheDesktop()
        {
            // A 21:9 capture presented on a 16:9 desktop is the shape mismatch
            // that produced bars down the sides.
            BitmapSource source = CreateSolidBitmap(2560, 1080, White);

            BitmapSource result = WallpaperPlayer.CentreCropToAspect(source, DesktopAspect);

            Assert.Equal(1920, result.PixelWidth);
            Assert.Equal(1080, result.PixelHeight);
            AssertAspectRatio(result, DesktopAspect);
        }

        [WpfFact]
        public void CentreCrop_TrimsTopAndBottomWhenTheFrameIsTallerThanTheDesktop()
        {
            // Portrait/4:3 source on a widescreen desktop.
            BitmapSource source = CreateSolidBitmap(1200, 900, White);

            BitmapSource result = WallpaperPlayer.CentreCropToAspect(source, DesktopAspect);

            Assert.Equal(1200, result.PixelWidth);
            Assert.Equal(675, result.PixelHeight);
            AssertAspectRatio(result, DesktopAspect);
        }

        [WpfFact]
        public void CentreCrop_LeavesAnAlreadyMatchingFrameUntouched()
        {
            BitmapSource source = CreateSolidBitmap(1920, 1080, White);

            BitmapSource result = WallpaperPlayer.CentreCropToAspect(source, DesktopAspect);

            // No-op must not re-wrap the bitmap: the paused frame is held for
            // the whole suspension and re-encoding it would waste memory.
            Assert.Same(source, result);
        }

        [WpfFact]
        public void CentreCrop_IgnoresAnUnknownDesktopAspect()
        {
            BitmapSource source = CreateSolidBitmap(1920, 1080, White);

            // The host has not been laid out yet (window not attached), so there
            // is nothing trustworthy to crop against.
            Assert.Same(source, WallpaperPlayer.CentreCropToAspect(source, double.NaN));
            Assert.Same(source, WallpaperPlayer.CentreCropToAspect(source, 0));
            Assert.Same(source, WallpaperPlayer.CentreCropToAspect(source, -1.5));
        }

        [WpfFact]
        public void CentreCrop_PhysicallyRemovesBakedInSideBars()
        {
            // This is the defect, reproduced without any video: a capture that
            // is wider than the desktop and already has black bands baked into
            // its left and right edges.
            const int width = 1920;
            const int height = 800;
            const int band = 240;

            BitmapSource letterboxed = CreateSolidBitmap(
                width,
                height,
                (x, _) => x < band || x >= width - band ? Black : White);

            Assert.True(IsBlack(letterboxed, 0, height / 2), "precondition: left band is black");
            Assert.True(
                IsBlack(letterboxed, width - 1, height / 2),
                "precondition: right band is black");

            BitmapSource result = WallpaperPlayer.CentreCropToAspect(letterboxed, DesktopAspect);

            // 1422x800 centred inside 1920x800 starts at x=249, which is past
            // the 240px band on the left and stops before the band on the
            // right — the bars are gone from the pixels, not merely hidden.
            Assert.Equal(1422, result.PixelWidth);
            Assert.Equal(800, result.PixelHeight);
            Assert.False(
                IsBlack(result, 0, result.PixelHeight / 2),
                "cropped frame must not start with a black column (left bar survived)");
            Assert.False(
                IsBlack(result, result.PixelWidth - 1, result.PixelHeight / 2),
                "cropped frame must not end with a black column (right bar survived)");
        }

        [WpfFact]
        public void CentreCrop_KeepsTheCentreOfTheFrame()
        {
            // Cropping must be centred: a wallpaper that jumps sideways when it
            // pauses is as visible as one with bars.
            const int width = 2560;
            const int height = 1080;

            BitmapSource source = CreateSolidBitmap(
                width,
                height,
                (x, _) => x >= width / 2 - 100 && x < width / 2 + 100 ? Black : White);

            BitmapSource result = WallpaperPlayer.CentreCropToAspect(source, DesktopAspect);

            int centreX = result.PixelWidth / 2;
            int centreY = result.PixelHeight / 2;
            Assert.True(
                IsBlack(result, centreX, centreY),
                "the centre marker must survive a centred crop");
            Assert.False(
                IsBlack(result, 0, centreY),
                "the left edge must be background, not the centre marker");
        }

        private static void AssertAspectRatio(BitmapSource source, double expected)
        {
            double actual = (double)source.PixelWidth / source.PixelHeight;
            Assert.True(
                Math.Abs(actual - expected) <= expected * 0.01,
                $"expected aspect {expected:0.####} but got {actual:0.####} " +
                $"({source.PixelWidth}x{source.PixelHeight})");
        }

        /// <summary>
        /// Builds a frozen BGRA32 bitmap from a per-pixel callback. No video
        /// decoder is involved, so this runs in CI.
        /// </summary>
        private static BitmapSource CreateSolidBitmap(int width, int height, uint argb)
            => CreateSolidBitmap(width, height, (_, _) => argb);

        private static BitmapSource CreateSolidBitmap(
            int width, int height, Func<int, int, uint> pixelAt)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
            int stride = width * 4;
            var buffer = new byte[stride * height];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    uint argb = pixelAt(x, y);
                    int offset = (y * stride) + (x * 4);
                    buffer[offset + 0] = (byte)(argb & 0xFF);          // B
                    buffer[offset + 1] = (byte)((argb >> 8) & 0xFF);   // G
                    buffer[offset + 2] = (byte)((argb >> 16) & 0xFF);  // R
                    buffer[offset + 3] = (byte)((argb >> 24) & 0xFF);  // A
                }
            }

            bitmap.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride, 0);
            bitmap.Freeze();
            return bitmap;
        }

        private static bool IsBlack(BitmapSource source, int x, int y)
        {
            var pixel = new byte[4];
            source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
            return pixel[0] < 8 && pixel[1] < 8 && pixel[2] < 8;
        }
    }
}
