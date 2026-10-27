using System;
using System.Windows.Media;

namespace Intonaco.Core
{
    /// <summary>
    /// Frame-interval gate for the wallpaper presentation path.
    ///
    /// It serves two real purposes:
    ///  1. It owns the frame interval for the configured target frame rate
    ///     (30 fps -> ~33.3 ms, 60 fps -> ~16.6 ms) and answers
    ///     "may this composition tick present a frame?" for callers that run
    ///     on <see cref="CompositionTarget.Rendering"/>.
    ///  2. It measures the presentation rate actually achieved while playing,
    ///     so the frame-rate preference can be verified in
    ///     %LocalAppData%\Intonaco\debug.log instead of being taken on faith.
    ///
    /// The limiter itself never blocks the decoder; the decoder-side cap is
    /// applied by <see cref="WallpaperPlayer"/> through the WPF media
    /// timeline's DesiredFrameRate. Together they are what makes a 30 fps
    /// setting measurably cheaper than 60 fps.
    /// </summary>
    public sealed class FrameRateLimiter : IDisposable
    {
        private const double MeasuredWindowSeconds = 2.0;

        private int _targetFps = 30;
        private TimeSpan _frameInterval = TimeSpan.FromSeconds(1.0 / 30.0);
        private TimeSpan _lastRenderTime = TimeSpan.MinValue;

        private TimeSpan _measurementStart = TimeSpan.MinValue;
        private int _measurementFrames;
        private double _measuredFps;
        private bool _disposed;

        public FrameRateLimiter(int targetFps = 30)
        {
            TargetFps = targetFps;
        }

        public int TargetFps
        {
            get => _targetFps;
            set
            {
                _targetFps = Math.Clamp(value, 1, 60);
                _frameInterval = TimeSpan.FromSeconds(1.0 / _targetFps);
            }
        }

        /// <summary>Minimum interval between two presented frames.</summary>
        public TimeSpan FrameInterval => _frameInterval;

        /// <summary>
        /// Presentation rate observed over the last measurement window
        /// (0 until the first window completes).
        /// </summary>
        public double MeasuredFps => _measuredFps;

        /// <summary>
        /// Returns true when enough time has elapsed for the next frame.
        /// Call this from CompositionTarget.Rendering to decide whether the
        /// tick is allowed to present.
        /// </summary>
        public bool ShouldRender(RenderingEventArgs e)
        {
            if (e == null) return false;
            return ShouldRender(e.RenderingTime);
        }

        /// <summary>
        /// Wall-clock variant used by timer-driven callers and by tests.
        /// </summary>
        public bool ShouldRender(TimeSpan now)
        {
            if (_disposed) return false;

            if (_lastRenderTime == TimeSpan.MinValue || now - _lastRenderTime >= _frameInterval)
            {
                _lastRenderTime = now;
                RecordPresentedFrame(now);
                return true;
            }

            return false;
        }

        private void RecordPresentedFrame(TimeSpan now)
        {
            _measurementFrames++;

            if (_measurementStart == TimeSpan.MinValue)
            {
                _measurementStart = now;
                return;
            }

            double elapsed = (now - _measurementStart).TotalSeconds;
            if (elapsed < MeasuredWindowSeconds)
                return;

            _measuredFps = _measurementFrames / elapsed;
            _measurementStart = now;
            _measurementFrames = 0;
        }

        /// <summary>Clears gate and measurement state (new media session).</summary>
        public void Reset()
        {
            _lastRenderTime = TimeSpan.MinValue;
            _measurementStart = TimeSpan.MinValue;
            _measurementFrames = 0;
            _measuredFps = 0.0;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
