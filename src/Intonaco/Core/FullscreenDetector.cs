using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Intonaco.Core
{
    /// <summary>
    /// Detects when a fullscreen application is running so wallpaper
    /// playback can be paused to save resources.
    /// Uses a low-frequency timer rather than aggressive polling.
    /// </summary>
    public sealed class FullscreenDetector : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_MAXIMIZE = 0x01000000;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_BORDER = 0x00800000;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        private DispatcherTimer? _timer;
        private bool _isFullscreen;
        private bool _disposed;

        // Check every 2 seconds — balances responsiveness with CPU usage
        private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

        public event Action<bool>? FullscreenStateChanged;
        public bool IsFullscreenActive => _isFullscreen;
        public bool IsEnabled { get; set; } = true;
        public bool IsRunning => _timer?.IsEnabled == true;

        public void Start()
        {
            if (_disposed || _timer != null) return;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = CheckInterval
            };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= OnTimerTick;
                _timer = null;
            }
        }

        private void OnTimerTick(object? sender, EventArgs e)
        {
            if (!IsEnabled)
            {
                if (_isFullscreen)
                {
                    _isFullscreen = false;
                    FullscreenStateChanged?.Invoke(false);
                }
                return;
            }

            bool fullscreen = DetectFullscreen();
            if (fullscreen != _isFullscreen)
            {
                _isFullscreen = fullscreen;
                FullscreenStateChanged?.Invoke(fullscreen);
            }
        }

        private bool DetectFullscreen()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            // Ignore desktop and shell windows
            var className = new System.Text.StringBuilder(256);
            GetClassName(foreground, className, 256);
            string cls = className.ToString();

            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd"
                or "Shell_SecondaryTrayWnd" or "Windows.UI.Core.CoreWindow")
                return false;

            // Check if the foreground window covers the entire monitor
            if (!GetWindowRect(foreground, out RECT windowRect))
                return false;

            IntPtr monitor = MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST);
            if (monitor == IntPtr.Zero) return false;

            var monitorInfo = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return false;

            RECT screenRect = monitorInfo.rcMonitor;

            // The window is fullscreen if it covers the entire monitor
            bool coversScreen =
                windowRect.Left <= screenRect.Left &&
                windowRect.Top <= screenRect.Top &&
                windowRect.Right >= screenRect.Right &&
                windowRect.Bottom >= screenRect.Bottom;

            if (!coversScreen) return false;

            // Additional check: if window has no caption/border, it's likely truly fullscreen
            int style = GetWindowLong(foreground, GWL_STYLE);
            bool hasCaption = (style & WS_CAPTION) == WS_CAPTION;

            // Maximized windows with captions are regular maximized apps, not fullscreen
            if (hasCaption && (style & WS_MAXIMIZE) == WS_MAXIMIZE)
                return false;

            return true;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
