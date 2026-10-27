using System;

namespace Intonaco.Core
{
    /// <summary>
    /// Pure decision logic that keeps the three playback concepts independent:
    /// an explicit manual pause, focus-driven automatic pause, and background
    /// rendering (which is always active). UI-free so the interactions can be
    /// unit-tested without a Windows desktop session.
    /// </summary>
    internal static class PlaybackFocusPolicy
    {
        /// <summary>
        /// Focus may stop animation updates only when Mode A is selected, the
        /// user has not paused manually, and animation is currently running.
        /// Rendering is never gated here: the wallpaper stays visible.
        /// </summary>
        public static bool CanAutoPause(
            bool isManualPause,
            bool isFocusAutoPauseEnabled,
            bool isAnimating)
            => !isManualPause && isFocusAutoPauseEnabled && isAnimating;

        /// <summary>
        /// Focus may restart animation only for a pause that focus/system
        /// events themselves own. A user-owned pause is never resumed here.
        /// </summary>
        public static bool CanAutoResume(bool isManualPause, bool noOtherPauseOwners)
            => !isManualPause && noOtherPauseOwners;

        /// <summary>
        /// Switching to Mode B releases a focus-owned pause, but must not
        /// resume a manual pause or a lock/sleep pause.
        /// </summary>
        public static bool CanApplyContinueMode(
            bool isManualPause,
            bool focusOwnedPause,
            bool systemOwnedPause,
            bool isPaused)
            => !isManualPause && focusOwnedPause && !systemOwnedPause && isPaused;
    }
}
