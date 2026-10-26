using System;
using System.Windows.Media;

namespace Intonaco.Core
{
    /// <summary>
    /// Throttles WPF rendering to a target frame rate by selectively
    /// skipping CompositionTarget.Rendering cycles. This reduces GPU
    /// and CPU usage for wallpaper playback where high frame rates
    /// are unnecessary.
    /// </summary>
    public sealed class FrameRateLimiter : IDisposable
    {
        private int _targetFps = 30;
        private TimeSpan _frameInterval;
        private TimeSpan _lastRenderTime;
        private bool _disposed;

        public int TargetFps
        {
            get => _targetFps;
            set
            {
                _targetFps = Math.Clamp(value, 1, 60);
                _frameInterval = TimeSpan.FromSeconds(1.0 / _targetFps);
            }
        }

        public FrameRateLimiter(int targetFps = 30)
        {
            TargetFps = targetFps;
        }

        /// <summary>
        /// Returns true if enough time has elapsed for the next frame.
        /// Call this from CompositionTarget.Rendering to decide whether to render.
        /// </summary>
        public bool ShouldRender(RenderingEventArgs e)
        {
            if (_disposed) return false;

            var renderTime = e.RenderingTime;
            if (renderTime - _lastRenderTime >= _frameInterval)
            {
                _lastRenderTime = renderTime;
                return true;
            }
            return false;
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
