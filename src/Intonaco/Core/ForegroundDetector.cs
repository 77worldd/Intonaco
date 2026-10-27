using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Intonaco.Core
{
    /// <summary>
    /// Detects when the Windows foreground window is not the desktop
    /// and triggers auto-pause to save resources. This implements the
    /// "Pause when other apps are focused" feature matching Lively
    /// Wallpaper behaviour: wallpaper plays only when the desktop
    /// (Explorer) or Intonaco itself is in the foreground.
    ///
    /// Uses a low-frequency 1.2s poll; no hooks, no active-win npm
    /// package, no aggressive CPU usage.
    /// </summary>
    public sealed class ForegroundDetector : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        private static readonly TimeSpan CheckInterval = TimeSpan.FromMilliseconds(1200);

        private DispatcherTimer? _timer;
        private bool _disposed;
        private bool _isDesktopForeground = true;

        public event Action<bool>? ForegroundStateChanged;

        /// <summary>
        /// True when the desktop (Explorer) or Intonaco is in the foreground
        /// and wallpaper should be playing. False when another app covers the
        /// desktop and wallpaper should be paused.
        /// </summary>
        public bool IsDesktopForeground => _isDesktopForeground;

        public bool IsEnabled { get; set; } = true;
        public bool IsRunning => _timer?.IsEnabled == true;

        public void Start()
        {
            if (_disposed) return;
            if (_timer != null) return;

            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = CheckInterval
            };
            _timer.Tick += OnTick;
            _timer.Start();

            // Immediate check so we don't wait 1.2s on startup.
            EvaluateAndNotify();
        }

        public void Stop()
        {
            if (_timer != null)
            {
                _timer.Tick -= OnTick;
                _timer.Stop();
                _timer = null;
            }

            // Mode B stops the detector. Forget the last focus state so a
            // later switch back to Mode A while another app is focused still
            // raises a fresh "not desktop" event and pauses animation.
            _isDesktopForeground = true;
        }

        private void OnTick(object? sender, EventArgs e)
        {
            if (!IsEnabled)
            {
                // If disabled, treat as desktop foreground so wallpaper keeps playing.
                if (!_isDesktopForeground)
                {
                    _isDesktopForeground = true;
                    ForegroundStateChanged?.Invoke(true);
                }
                return;
            }

            EvaluateAndNotify();
        }

        private void EvaluateAndNotify()
        {
            bool desktop = IsForegroundDesktop(out _);
            if (desktop != _isDesktopForeground)
            {
                _isDesktopForeground = desktop;
                ForegroundStateChanged?.Invoke(desktop);
            }
        }

        /// <summary>
        /// Returns true if the foreground window belongs to the desktop
        /// (Explorer) or to Intonaco itself, meaning wallpaper should play.
        /// Returns false if another app is in the foreground and wallpaper
        /// should pause.
        /// </summary>
        public static bool IsForegroundDesktop(out string foregroundProcessName)
        {
            foregroundProcessName = string.Empty;

            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero)
                return true; // No foreground window -> desktop visible

            // Check window class: these are always desktop/shell surfaces.
            var sb = new StringBuilder(256);
            GetClassName(fg, sb, sb.Capacity);
            string cls = sb.ToString();

            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                or "Windows.UI.Core.CoreWindow" or "XamlExplorerHostIslandWindow")
            {
                foregroundProcessName = cls;
                return true;
            }

            // Resolve owning process name.
            uint pid = 0;
            GetWindowThreadProcessId(fg, out pid);
            if (pid == 0)
                return true;

            try
            {
                using var proc = Process.GetProcessById((int)pid);
                string name = proc.ProcessName ?? string.Empty;
                foregroundProcessName = name;
                return ShouldPlayForForegroundProcess(name);
            }
            catch
            {
                // If we can't resolve the process, assume desktop to avoid false pauses.
                return true;
            }
        }

        /// <summary>
        /// Decides whether the wallpaper should keep playing for a given
        /// foreground process name. Shell/desktop surfaces and Intonaco itself
        /// count as "desktop visible"; everything else (browsers, Discord,
        /// Steam, games, editors) means the wallpaper is hidden behind a real
        /// application and should be suspended.
        /// </summary>
        internal static bool ShouldPlayForForegroundProcess(string? processName)
        {
            if (string.IsNullOrWhiteSpace(processName))
                return true;

            return processName.Trim().ToLowerInvariant() switch
            {
                // Explorer owns the desktop, taskbar and the icon layer.
                "explorer" => true,

                // Transient shell overlays: Start menu, Search, Action Center,
                // UWP host frames. They do not hide the desktop for long and
                // pausing for them would flicker the wallpaper.
                "shellexperiencehost" => true,
                "startmenuexperiencehost" => true,
                "searchhost" => true,
                "searchui" => true,
                "searchapp" => true,
                "applicationframehost" => true,
                "systemsettings" => true,
                "lockapp" => true,
                "tabtip" => true,
                "textinputhost" => true,

                // Our own UI: the user is adjusting the wallpaper, keep it live.
                "intonaco" => true,

                _ => false
            };
        }

        /// <summary>
        /// Fullscreen detection helper kept for legacy compatibility.
        /// Checks if the foreground window covers the entire monitor and
        /// has no caption, indicating a fullscreen game/video.
        /// </summary>
        public static bool IsForegroundFullscreen()
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            var sb = new StringBuilder(256);
            GetClassName(fg, sb, 256);
            string cls = sb.ToString();
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return false;

            if (!GetWindowRect(fg, out RECT winRect))
                return false;

            IntPtr mon = MonitorFromWindow(fg, 2);
            if (mon == IntPtr.Zero) return false;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(mon, ref mi))
                return false;

            RECT screen = mi.rcMonitor;
            bool coversScreen =
                winRect.Left <= screen.Left &&
                winRect.Top <= screen.Top &&
                winRect.Right >= screen.Right &&
                winRect.Bottom >= screen.Bottom;

            return coversScreen;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
        }
    }
}
