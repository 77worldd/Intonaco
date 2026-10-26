using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;

namespace Intonaco.Core
{
    /// <summary>
    /// Integrates a WPF window into the Windows desktop wallpaper layer using
    /// the WorkerW technique: the window is parented behind the desktop icons
    /// (SHELLDLL_DefView) but above the static wallpaper.
    ///
    /// Explorer's window layout is undocumented and differs between Windows
    /// builds, so discovery tries several strategies in order and attaches to
    /// the first candidate that accepts the player window:
    ///   1. ShellHostNextSibling — the classic layout: a top-level window
    ///      hosts SHELLDLL_DefView and the next top-level WorkerW after it
    ///      is the wallpaper layer.
    ///   2. ProgmanChild — newer Windows 11 builds (incl. Spotlight setups):
    ///      the spawned WorkerW is a *child* of Progman.
    ///   3. ProgmanNextSibling — next top-level WorkerW after Progman.
    ///   4. AnyWorkerW — any top-level WorkerW that does not host the icons.
    ///   5. ProgmanFallback — parent directly to Progman, Z-ordered to the
    ///      bottom so the icon view stays on top.
    /// Every attempt is logged to %LocalAppData%\Intonaco\debug.log.
    /// </summary>
    public sealed class DesktopIntegration : IDisposable
    {
        #region Native Methods

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
            string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg,
            IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowLongPtrW")]
        private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongPtrW")]
        private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const uint SMTO_NORMAL = 0x0000;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const long WS_POPUP = unchecked((long)0x80000000);
        private const long WS_CHILD = 0x40000000;
        private const long WS_VISIBLE = 0x10000000;
        private const long WS_CAPTION = 0x00C00000;
        private const long WS_SYSMENU = 0x00080000;
        private const long WS_THICKFRAME = 0x00040000;
        private const long WS_EX_NOACTIVATE = 0x08000000;
        private const long WS_EX_TOOLWINDOW = 0x00000080;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_SHOW = 5;
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);

        #endregion

        #region Candidate ordering (pure logic, unit-tested)

        internal enum WorkerWStrategy
        {
            ShellHostNextSibling = 0,
            ProgmanChild = 1,
            ProgmanNextSibling = 2,
            AnyWorkerW = 3,
            ProgmanFallback = 4
        }

        internal readonly struct WorkerWCandidate
        {
            public WorkerWCandidate(IntPtr handle, WorkerWStrategy strategy, long area, bool isVisible)
            {
                Handle = handle;
                Strategy = strategy;
                Area = area;
                IsVisible = isVisible;
            }

            public IntPtr Handle { get; }
            public WorkerWStrategy Strategy { get; }
            public long Area { get; }
            public bool IsVisible { get; }
        }

        /// <summary>
        /// De-duplicates candidates (keeping each handle's best strategy) and
        /// orders them by strategy priority, then visibility, then size.
        /// </summary>
        internal static IReadOnlyList<IntPtr> OrderCandidates(IEnumerable<WorkerWCandidate> candidates)
        {
            return candidates
                .Where(c => c.Handle != IntPtr.Zero)
                .GroupBy(c => c.Handle)
                .Select(g => g.OrderBy(c => (int)c.Strategy).First())
                .OrderBy(c => (int)c.Strategy)
                .ThenByDescending(c => c.IsVisible)
                .ThenByDescending(c => c.Area)
                .Select(c => c.Handle)
                .ToList();
        }

        #endregion

        private IntPtr _workerW = IntPtr.Zero;
        private IntPtr _playerHandle = IntPtr.Zero;
        private Window? _playerWindow;
        private bool _disposed;
        private bool _isAttached;
        private bool _attachedToProgman;

        public bool IsAttached => _isAttached && _workerW != IntPtr.Zero;

        /// <summary>Human-readable reason for the last failed Attach, if any.</summary>
        public string? LastError { get; private set; }

        /// <summary>
        /// Asks Explorer to spawn the wallpaper WorkerW by sending the
        /// undocumented 0x052C message to Progman. Different Windows builds
        /// respond to different (wParam, lParam) variants; send them all —
        /// extra sends are harmless once the WorkerW exists.
        /// </summary>
        private static void SpawnWorkerW(IntPtr progman)
        {
            SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), IntPtr.Zero, SMTO_NORMAL, 1000, out _);
            SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), SMTO_NORMAL, 1000, out _);
            SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);
        }

        private static string GetClassNameString(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetClassName(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private static WorkerWCandidate MakeCandidate(IntPtr handle, WorkerWStrategy strategy)
        {
            long area = 0;
            if (handle != IntPtr.Zero && GetWindowRect(handle, out RECT rect))
                area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            bool visible = handle != IntPtr.Zero && IsWindowVisible(handle);
            return new WorkerWCandidate(handle, strategy, area, visible);
        }

        private static void LogDesktopWindows(IReadOnlyList<(IntPtr Handle, string ClassName)> topLevels)
        {
            foreach (var (handle, className) in topLevels)
            {
                if (className is not ("Progman" or "WorkerW"))
                    continue;

                IntPtr shellView = FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null);
                GetWindowRect(handle, out RECT rect);
                DebugLog.Write(
                    $"  top-level 0x{handle.ToInt64():X} {className} " +
                    $"shellView={(shellView != IntPtr.Zero)} visible={IsWindowVisible(handle)} " +
                    $"rect=({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})");
            }
        }

        /// <summary>
        /// Finds every plausible wallpaper-layer window, best candidate first.
        /// Retries briefly because Explorer creates the WorkerW asynchronously
        /// after the 0x052C message (and right after an Explorer restart).
        /// </summary>
        private IReadOnlyList<IntPtr> FindWorkerWCandidates()
        {
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
            {
                DebugLog.Write("FindWorkerWCandidates: Progman window not found (Explorer not running?).");
                return Array.Empty<IntPtr>();
            }

            SpawnWorkerW(progman);

            IReadOnlyList<IntPtr> ordered = Array.Empty<IntPtr>();
            for (int attempt = 0; attempt < 6; attempt++)
            {
                var topLevels = new List<(IntPtr, string)>();
                EnumWindowsProc callback = (hWnd, _) =>
                {
                    topLevels.Add((hWnd, GetClassNameString(hWnd)));
                    return true;
                };
                EnumWindows(callback, IntPtr.Zero);
                GC.KeepAlive(callback);

                if (attempt == 0)
                    LogDesktopWindows(topLevels);

                var candidates = new List<WorkerWCandidate>();

                // 1. Classic: window hosting SHELLDLL_DefView -> next WorkerW sibling.
                foreach (var (handle, _) in topLevels)
                {
                    if (FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                    {
                        IntPtr next = FindWindowEx(IntPtr.Zero, handle, "WorkerW", null);
                        if (next != IntPtr.Zero)
                            candidates.Add(MakeCandidate(next, WorkerWStrategy.ShellHostNextSibling));
                    }
                }

                // 2. Newer Windows 11: WorkerW is a child of Progman.
                IntPtr child = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
                if (child != IntPtr.Zero)
                    candidates.Add(MakeCandidate(child, WorkerWStrategy.ProgmanChild));

                // 3. Next top-level WorkerW after Progman.
                IntPtr afterProgman = FindWindowEx(IntPtr.Zero, progman, "WorkerW", null);
                if (afterProgman != IntPtr.Zero)
                    candidates.Add(MakeCandidate(afterProgman, WorkerWStrategy.ProgmanNextSibling));

                // 4. Any top-level WorkerW that does not host the desktop icons.
                foreach (var (handle, className) in topLevels)
                {
                    if (className == "WorkerW" &&
                        FindWindowEx(handle, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero)
                    {
                        candidates.Add(MakeCandidate(handle, WorkerWStrategy.AnyWorkerW));
                    }
                }

                // 5. Last resort: Progman itself (player Z-ordered to bottom).
                candidates.Add(MakeCandidate(progman, WorkerWStrategy.ProgmanFallback));

                ordered = OrderCandidates(candidates);
                DebugLog.Write(
                    $"FindWorkerWCandidates attempt {attempt + 1}: " +
                    $"[{string.Join(", ", candidates.Select(c => $"{c.Strategy}=0x{c.Handle.ToInt64():X}"))}]");

                // Good enough: at least one real WorkerW (not just the fallback).
                bool hasWorkerW = candidates.Any(c => c.Strategy != WorkerWStrategy.ProgmanFallback);
                if (hasWorkerW)
                    return ordered;

                Thread.Sleep(200);
                SpawnWorkerW(progman);
            }

            DebugLog.Write("FindWorkerWCandidates: no WorkerW spawned; falling back to Progman only.");
            return ordered;
        }

        /// <summary>
        /// Attaches the given WPF window to the desktop wallpaper layer.
        /// Tries each candidate parent until one verifiably accepts the window.
        /// </summary>
        public bool Attach(Window playerWindow)
        {
            if (_disposed) return false;

            try
            {
                return AttachCore(playerWindow);
            }
            catch (Exception ex)
            {
                // e.g. the WPF window's handle was destroyed together with the
                // old WorkerW after an Explorer restart. Callers re-apply the
                // wallpaper with a fresh player window when Attach fails.
                LastError = $"Unexpected error attaching to desktop: {ex.Message}";
                DebugLog.Write($"Attach threw: {ex}");
                Detach();
                return false;
            }
        }

        private bool AttachCore(Window playerWindow)
        {
            LastError = null;
            Detach();

            var candidates = FindWorkerWCandidates();
            if (candidates.Count == 0)
            {
                LastError = "Desktop window (Progman) not found. Is Explorer running?";
                return false;
            }

            _playerWindow = playerWindow;

            var helper = new WindowInteropHelper(playerWindow);
            helper.EnsureHandle();
            _playerHandle = helper.Handle;
            if (_playerHandle == IntPtr.Zero)
            {
                LastError = "Could not create the player window.";
                DebugLog.Write("Attach: player HWND is null.");
                return false;
            }

            // Convert the overlapped/popup WPF window into a child window.
            // WS_POPUP and WS_CHILD are mutually exclusive — WS_POPUP must go.
            long style = GetWindowLongPtr(_playerHandle, GWL_STYLE).ToInt64();
            style = (style & ~WS_POPUP & ~WS_CAPTION & ~WS_SYSMENU & ~WS_THICKFRAME)
                    | WS_CHILD | WS_VISIBLE;
            SetWindowLongPtr(_playerHandle, GWL_STYLE, new IntPtr(style));

            long exStyle = GetWindowLongPtr(_playerHandle, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLongPtr(_playerHandle, GWL_EXSTYLE, new IntPtr(exStyle));

            int lastWin32Error = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                IntPtr candidate = candidates[i];
                if (!IsWindow(candidate))
                    continue;

                SetParent(_playerHandle, candidate);
                lastWin32Error = Marshal.GetLastWin32Error();
                IntPtr actualParent = GetParent(_playerHandle);
                DebugLog.Write(
                    $"Attach candidate #{i + 1} 0x{candidate.ToInt64():X} ({GetClassNameString(candidate)}): " +
                    $"parent after SetParent = 0x{actualParent.ToInt64():X}, win32 error = {lastWin32Error}");

                if (actualParent != candidate)
                    continue;

                _workerW = candidate;
                _attachedToProgman = GetClassNameString(candidate) == "Progman";
                ResizeToDesktop();
                ShowWindow(_playerHandle, SW_SHOW);
                _isAttached = true;
                DebugLog.Write($"Attach succeeded with candidate #{i + 1} (0x{candidate.ToInt64():X}).");
                return true;
            }

            _workerW = IntPtr.Zero;
            LastError =
                $"Windows refused to place the wallpaper on the desktop layer (Win32 error {lastWin32Error}).";
            DebugLog.Write("Attach failed: no candidate accepted the player window.");
            return false;
        }

        /// <summary>
        /// Resizes the player window to cover the full desktop area.
        /// </summary>
        public void ResizeToDesktop()
        {
            if (_playerHandle == IntPtr.Zero || _workerW == IntPtr.Zero)
                return;

            if (GetWindowRect(_workerW, out RECT rect))
            {
                int width = rect.Right - rect.Left;
                int height = rect.Bottom - rect.Top;
                // On the Progman fallback the player shares a parent with the
                // icon view (SHELLDLL_DefView); keep it at the bottom.
                IntPtr insertAfter = _attachedToProgman ? HWND_BOTTOM : IntPtr.Zero;
                SetWindowPos(_playerHandle, insertAfter, 0, 0, width, height,
                    SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }
        }

        /// <summary>
        /// Reattaches to desktop after Explorer restart or display change.
        /// </summary>
        public bool Reattach()
        {
            if (_disposed || _playerWindow == null)
                return false;

            if (_workerW != IntPtr.Zero && IsWindow(_workerW) &&
                _playerHandle != IntPtr.Zero && IsWindow(_playerHandle) &&
                GetParent(_playerHandle) == _workerW)
            {
                ResizeToDesktop();
                return true;
            }

            // WorkerW (or our child window) was destroyed — full re-discovery.
            return Attach(_playerWindow);
        }

        /// <summary>
        /// Detaches the player window from the desktop.
        /// </summary>
        public void Detach()
        {
            if (_playerHandle != IntPtr.Zero && IsWindow(_playerHandle))
            {
                SetParent(_playerHandle, IntPtr.Zero);
            }

            _isAttached = false;
            _attachedToProgman = false;
            _workerW = IntPtr.Zero;
            _playerHandle = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Detach();
        }
    }
}
