using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;

namespace Intonaco.Tests
{
    public class ThemeTests
    {
        private static ResourceDictionary LoadTheme()
        {
            return new ResourceDictionary
            {
                Source = new Uri(
                    "pack://application:,,,/Intonaco;component/Themes/IntonacoTheme.xaml",
                    UriKind.Absolute)
            };
        }

        [WpfFact]
        public void SliderStyle_AppliesNativeTemplateAndBehavior()
        {
            var theme = LoadTheme();
            var style = Assert.IsType<Style>(theme["IntonacoSlider"]);
            var slider = new Slider { Style = style, Width = 240 };

            Assert.True(slider.ApplyTemplate());
            var track = Assert.IsType<Track>(
                slider.Template.FindName("PART_Track", slider));

            Assert.NotNull(track.DecreaseRepeatButton);
            Assert.NotNull(track.IncreaseRepeatButton);
            Assert.NotNull(track.Thumb);
            Assert.Equal(22, track.Thumb.Width);
            Assert.Equal(22, track.Thumb.Height);
            Assert.Equal(32, slider.Height);
            Assert.True(slider.IsMoveToPointEnabled);
            Assert.True(slider.IsSnapToTickEnabled);
            Assert.Equal(1, slider.SmallChange);
            Assert.Equal(10, slider.LargeChange);
        }

        [WpfFact]
        public void ToggleStyle_UsesCompactSettingsSwitch()
        {
            var theme = LoadTheme();
            var style = Assert.IsType<Style>(theme["IntonacoToggle"]);
            var toggle = new CheckBox { Style = style, Content = "Setting" };

            Assert.True(toggle.ApplyTemplate());
            var track = Assert.IsType<Border>(
                toggle.Template.FindName("Track", toggle));
            var thumb = Assert.IsType<Border>(
                toggle.Template.FindName("Thumb", toggle));
            var switchGrid = Assert.IsType<Grid>(track.Parent);

            Assert.Equal(40, switchGrid.Width);
            Assert.Equal(20, switchGrid.Height);
            Assert.Equal(12, thumb.Width);
            Assert.Equal(12, thumb.Height);
        }

        [WpfTheory]
        [InlineData("intonaco.ico")]
        [InlineData("intonaco-mark-light.png")]
        [InlineData("intonaco-mark-dark.png")]
        public void BrandAssets_AreEmbedded(string fileName)
        {
            var uri = new Uri(
                $"pack://application:,,,/Intonaco;component/Assets/{fileName}",
                UriKind.Absolute);

            var info = Application.GetResourceStream(uri);

            Assert.NotNull(info);
            using var stream = info.Stream;
            Assert.True(stream.Length > 0);
        }
    }
}
