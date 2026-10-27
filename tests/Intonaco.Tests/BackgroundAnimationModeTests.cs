using System;
using System.Text.Json;
using Intonaco.Core;
using Xunit;

namespace Intonaco.Tests
{
    /// <summary>
    /// The optional background-animation setting: two persisted modes plus
    /// the guarantee that focus events never override an explicit manual
    /// pause. Rendering is deliberately not part of these decisions.
    /// </summary>
    public class BackgroundAnimationModeTests
    {
        [Fact]
        public void Default_IsModeA_PauseWhenTheAppLosesFocus()
        {
            var settings = new AppSettings();

            Assert.Equal(AppSettings.PauseWhenUnfocusedMode, settings.BackgroundAnimationMode);
            Assert.True(settings.PauseAnimationsWhenUnfocused);
            Assert.True(settings.IsAutoPauseEnabled);
            Assert.True(PlaybackFocusPolicy.CanAutoPause(
                isManualPause: false, isFocusAutoPauseEnabled: true, isAnimating: true));
        }

        [Fact]
        public void ModeB_KeepsAnimating_AndDisablesFocusAutoPause()
        {
            var settings = new AppSettings
            {
                BackgroundAnimationMode = AppSettings.ContinueWhenUnfocusedMode
            };
            settings.Normalize();

            Assert.False(settings.PauseAnimationsWhenUnfocused);
            Assert.False(settings.IsAutoPauseEnabled);
            Assert.False(PlaybackFocusPolicy.CanAutoPause(
                isManualPause: false,
                isFocusAutoPauseEnabled: settings.IsAutoPauseEnabled,
                isAnimating: true));
        }

        [Fact]
        public void ModeSelection_SurvivesAnApplicationRestart()
        {
            var saved = new AppSettings
            {
                BackgroundAnimationMode = AppSettings.ContinueWhenUnfocusedMode
            };

            // Mirrors AppSettings.Save()/Load(): camelCase JSON in
            // %LocalAppData%\Intonaco\settings.json.
            var options = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            };
            string json = JsonSerializer.Serialize(saved, options);

            Assert.Contains("backgroundAnimationMode", json);

            AppSettings restored = AppSettings.LoadFromJson(json);

            Assert.Equal(AppSettings.ContinueWhenUnfocusedMode, restored.BackgroundAnimationMode);
            Assert.False(restored.PauseAnimationsWhenUnfocused);
            Assert.False(restored.IsAutoPauseEnabled);
        }

        [Fact]
        public void HandEditedOrLegacyMode_FallsBackToTheSafeDefault()
        {
            Assert.Equal(
                AppSettings.PauseWhenUnfocusedMode,
                AppSettings.NormalizeBackgroundAnimationMode("nonsense"));
            Assert.Equal(
                AppSettings.ContinueWhenUnfocusedMode,
                AppSettings.NormalizeBackgroundAnimationMode("continuewhenunfocused"));

            var settings = new AppSettings { BackgroundAnimationMode = "bogus" };
            settings.Normalize();

            Assert.True(settings.PauseAnimationsWhenUnfocused);
            Assert.True(settings.IsAutoPauseEnabled);
        }

        [Fact]
        public void OlderSettingsFile_WithoutTheMode_KeepsItsPreference()
        {
            var pause = AppSettings.LoadFromJson("""{ "pauseWhenUnfocused": true }""");
            var animate = AppSettings.LoadFromJson("""{ "pauseWhenUnfocused": false }""");

            Assert.Equal(AppSettings.PauseWhenUnfocusedMode, pause.BackgroundAnimationMode);
            Assert.Equal(AppSettings.ContinueWhenUnfocusedMode, animate.BackgroundAnimationMode);
            Assert.True(pause.IsAutoPauseEnabled);
            Assert.False(animate.IsAutoPauseEnabled);
        }

        [Fact]
        public void ManualPause_IsIndependentOfBothModes()
        {
            // Mode A: a focus event cannot auto-pause or auto-resume a
            // wallpaper the user paused explicitly.
            Assert.False(PlaybackFocusPolicy.CanAutoPause(
                isManualPause: true, isFocusAutoPauseEnabled: true, isAnimating: true));
            Assert.False(PlaybackFocusPolicy.CanAutoResume(
                isManualPause: true, noOtherPauseOwners: true));

            // Mode B: switching modes never resumes a manual pause.
            Assert.False(PlaybackFocusPolicy.CanApplyContinueMode(
                isManualPause: true, focusOwnedPause: true,
                systemOwnedPause: false, isPaused: true));

            // Mode B does release a pause that focus itself owns.
            Assert.True(PlaybackFocusPolicy.CanApplyContinueMode(
                isManualPause: false, focusOwnedPause: true,
                systemOwnedPause: false, isPaused: true));

            // ...but not a lock/sleep pause or a pause that is not paused.
            Assert.False(PlaybackFocusPolicy.CanApplyContinueMode(
                isManualPause: false, focusOwnedPause: true,
                systemOwnedPause: true, isPaused: true));
            Assert.False(PlaybackFocusPolicy.CanApplyContinueMode(
                isManualPause: false, focusOwnedPause: true,
                systemOwnedPause: false, isPaused: false));
        }

        [Fact]
        public void ModeB_ManualPauseStillWorks()
        {
            // The manual pause control does not consult the focus mode at
            // all; a manual pause can always be made and released.
            foreach (string mode in new[]
                     {
                         AppSettings.PauseWhenUnfocusedMode,
                         AppSettings.ContinueWhenUnfocusedMode
                     })
            {
                var settings = new AppSettings { BackgroundAnimationMode = mode };
                settings.Normalize();

                Assert.Equal(
                    mode == AppSettings.PauseWhenUnfocusedMode,
                    settings.IsAutoPauseEnabled);
                Assert.False(PlaybackFocusPolicy.CanAutoResume(
                    isManualPause: true, noOtherPauseOwners: true));
            }
        }
    }
}
