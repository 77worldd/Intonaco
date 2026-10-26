using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Intonaco.Core
{
    /// <summary>
    /// Integrates a WPF window into the Windows desktop wallpaper layer
    /// using the WorkerW technique. The window is parented behind desktop
    /// icons but above the static wallpaper.
    /// </summary>
    public sealed class DesktopIntegration : IDisposable
    {
        #region Native Methods

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter,
            string? lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg,
            UIntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const uint SMTO_NORMAL = 0x0000;
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CHILD = 0x40000000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const int SW_SHOW = 5;

        #endregion

        private IntPtr _workerW = IntPtr.Zero;
        private IntPtr _playerHandle = IntPtr.Zero;
        private Window? _playerWindow;
        private bool _disposed;
        private bool _isAttached;

        public bool IsAttached => _isAttached && _workerW != IntPtr.Zero;

        /// <summary>
        /// Finds or creates the WorkerW window used for desktop wallpaper rendering.
        /// </summary>
        private IntPtr GetWorkerW()
        {
            // Find the Progman window
            IntPtr progman = FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
                return IntPtr.Zero;

            // Send the undocumented message 0x052C to Progman.
            // This causes Windows to spawn a WorkerW window behind the desktop icons
            // that we can use as a parent for our wallpaper window.
            SendMessageTimeout(progman, 0x052C, UIntPtr.Zero, IntPtr.Zero,
                SMTO_NORMAL, 1000, out _);

            IntPtr workerW = IntPtr.Zero;

            // Enumerate all WorkerW windows to find the one that is a sibling
            // of Progman and contains the SHELLDLL_DefView (desktop icon container).
            IntPtr shellView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
            {
                // WorkerW is behind Progman in this case — find it
                workerW = FindWindowEx(IntPtr.Zero, progman, "WorkerW", null);
            }
            else
            {
                // SHELLDLL_DefView is hosted in a WorkerW. Find that WorkerW,
                // then get the next WorkerW sibling which is our target.
                IntPtr currentWorkerW = IntPtr.Zero;
                do
                {
                    currentWorkerW = FindWindowEx(IntPtr.Zero, currentWorkerW, "WorkerW", null);
                    if (currentWorkerW == IntPtr.Zero)
                        break;

                    shellView = FindWindowEx(currentWorkerW, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (shellView != IntPtr.Zero)
                    {
                        // Found the WorkerW that hosts SHELLDLL_DefView.
                        // Our target is the *next* WorkerW after this one.
                        workerW = FindWindowEx(IntPtr.Zero, currentWorkerW, "WorkerW", null);
                        break;
                    }
                } while (true);
            }

            return workerW;
        }

        /// <summary>
        /// Attaches the given WPF window to the desktop WorkerW layer.
        /// </summary>
        public bool Attach(Window playerWindow)
        {
            if (_disposed) return false;

            try
            {
                return AttachCore(playerWindow);
            }
            catch
            {
                // e.g. the WPF window's handle was destroyed together with the
                // old WorkerW after an Explorer restart. Callers re-apply the
                // wallpaper with a fresh player window when Attach fails.
                Detach();
                return false;
            }
        }

        private bool AttachCore(Window playerWindow)
        {
            Detach();

            _workerW = GetWorkerW();
            if (_workerW == IntPtr.Zero)
                return false;

            _playerWindow = playerWindow;

            // Get the WPF window's HWND
            var helper = new WindowInteropHelper(playerWindow);
            helper.EnsureHandle();
            _playerHandle = helper.Handle;

            if (_playerHandle == IntPtr.Zero)
                return false;

            // Remove standard window chrome styles and make it a child window
            int style = GetWindowLong(_playerHandle, GWL_STYLE);
            style = (style & ~0x00C00000 & ~0x00080000 & ~0x00040000) | WS_CHILD | WS_VISIBLE;
            SetWindowLong(_playerHandle, GWL_STYLE, style);

            int exStyle = GetWindowLong(_playerHandle, GWL_EXSTYLE);
            exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
            SetWindowLong(_playerHandle, GWL_EXSTYLE, exStyle);

            // Parent the player window to WorkerW
            SetParent(_playerHandle, _workerW);

            // Size it to fill the desktop work area
            ResizeToDesktop();

            ShowWindow(_playerHandle, SW_SHOW);

            _isAttached = true;
            return true;
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
                SetWindowPos(_playerHandle, IntPtr.Zero, 0, 0, width, height,
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

            // Check if WorkerW is still valid
            if (_workerW != IntPtr.Zero && IsWindow(_workerW))
            {
                ResizeToDesktop();
                return true;
            }

            // WorkerW was destroyed (Explorer restart) — reattach
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
