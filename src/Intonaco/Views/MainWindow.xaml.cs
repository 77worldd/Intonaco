using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Intonaco.Core;
using Microsoft.Win32;

namespace Intonaco.Views
{
    public partial class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly WallpaperPlayer _player;
        private readonly DesktopIntegration _desktop;
        private readonly FullscreenDetector _fullscreenDetector;
        private readonly SystemEventMonitor _systemEvents;
        private PlayerWindow? _playerWindow;

        private bool _isApplied;
        private bool _pausedByFullscreen;
        private bool _pausedBySystem;
        private bool _suppressEvents = true;
        private bool _isExiting;

        // Explorer restart detection
        private uint _shellRestartMessage;

        public MainWindow()
        {
            _settings = AppSettings.Load();
            _player = new WallpaperPlayer();
            _desktop = new DesktopIntegration();
            _fullscreenDetector = new FullscreenDetector();
            _systemEvents = new SystemEventMonitor();

            InitializeComponent();

            LoadSettings();
            WireEvents();
            InitializeSystemTray();

            // Handle startup args
            if (_settings.StartMinimized ||
                Environment.GetCommandLineArgs().Length > 1 &&
                Environment.GetCommandLineArgs()[1] == "--minimized")
            {
                WindowState = WindowState.Minimized;
                ShowInTaskbar = false;
                Hide();
            }

            // Auto-play last wallpaper
            if (_settings.AutoPlay && !string.IsNullOrEmpty(_settings.LastWallpaperPath) &&
                File.Exists(_settings.LastWallpaperPath))
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
                {
                    ApplyWallpaper(_settings.LastWallpaperPath);
                });
            }
        }

        private void LoadSettings()
        {
            _suppressEvents = true;

            PauseFullscreenToggle.IsChecked = _settings.PauseOnFullscreen;
            StartWithWindowsToggle.IsChecked = StartupManager.IsRegistered;
            VolumeSlider.Value = _settings.Volume * 100;

            // Set FPS combo
            foreach (ComboBoxItem item in FpsComboBox.Items)
            {
                if (item.Tag is string tag && int.TryParse(tag, out int fps) && fps == _settings.TargetFps)
                {
                    FpsComboBox.SelectedItem = item;
                    break;
                }
            }

            // Set theme combo
            foreach (ComboBoxItem item in ThemeComboBox.Items)
            {
                if (item.Tag is string theme && theme == _settings.Theme)
                {
                    ThemeComboBox.SelectedItem = item;
                    break;
                }
            }

            // Push persisted values into the player (fields only at this point;
            // the MediaElement is created on first ApplyWallpaper).
            _player.TargetFps = _settings.TargetFps;
            _player.Volume = _settings.Volume;
            _player.SetMute(_settings.IsMuted);
            UpdateMuteButton();

            _suppressEvents = false;
        }

        private void WireEvents()
        {
            _player.StateChanged += OnPlayerStateChanged;
            _player.ErrorOccurred += OnPlayerError;
            _player.MediaOpened += OnMediaOpened;

            _fullscreenDetector.FullscreenStateChanged += OnFullscreenChanged;
            _fullscreenDetector.IsEnabled = _settings.PauseOnFullscreen;

            _systemEvents.ShouldPause += OnSystemPause;
            _systemEvents.ShouldResume += OnSystemResume;
            _systemEvents.DisplayChanged += OnDisplayChanged;

            Loaded += OnWindowLoaded;
            Closing += OnWindowClosing;
            StateChanged += OnWindowStateChanged;
        }

        private void OnWindowLoaded(object sender, RoutedEventArgs e)
        {
            // Register for Explorer restart notification
            var source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            if (source != null)
            {
                _shellRestartMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");
                source.AddHook(WndProc);
            }

            _fullscreenDetector.Start();
            _systemEvents.Start();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_shellRestartMessage != 0 && msg == (int)_shellRestartMessage)
            {
                // Explorer restarted — reattach wallpaper
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                {
                    if (_isApplied && _playerWindow != null)
                    {
                        EnsureAttached();
                    }
                });
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void InitializeSystemTray()
        {
            // Icon will be set from embedded resource
            try
            {
                var iconUri = new Uri("pack://application:,,,/Assets/intonaco.ico", UriKind.Absolute);
                using var stream = Application.GetResourceStream(iconUri)?.Stream;
                if (stream != null)
                {
                    TrayIcon.Icon = new System.Drawing.Icon(stream);
                }
            }
            catch
            {
                // Will use default icon
            }

            TrayIcon.TrayMouseDoubleClick += (_, _) => ShowMainWindow();
        }

        #region Wallpaper Management

        private void OnChooseFile(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose wallpaper — Intonaco",
                Filter = "Video files|*.mp4;*.webm;*.avi;*.wmv;*.mkv;*.mov|" +
                         "All files|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (!string.IsNullOrEmpty(_settings.LastWallpaperPath))
            {
                var dir = Path.GetDirectoryName(_settings.LastWallpaperPath);
                if (dir != null && Directory.Exists(dir))
                    dialog.InitialDirectory = dir;
            }

            if (dialog.ShowDialog(this) == true)
            {
                ApplyWallpaper(dialog.FileName);
            }
        }

        private void ApplyWallpaper(string filePath)
        {
            ClearError();

            // Stop existing playback
            StopWallpaper();

            // Create player window if needed
            if (_playerWindow == null)
            {
                _playerWindow = new PlayerWindow();
                var mediaElement = _player.Initialize();
                _playerWindow.PlayerGrid.Children.Add(mediaElement);
                _playerWindow.Show();
            }

            // Attach to desktop
            if (!_desktop.Attach(_playerWindow))
            {
                ShowError("Failed to attach to desktop. Try restarting Explorer.");
                return;
            }

            // Start playback
            if (!_player.Play(filePath))
            {
                return; // Error event will fire
            }

            _player.SetMute(_settings.IsMuted);
            _player.Volume = _settings.Volume;
            _player.TargetFps = _settings.TargetFps;
            _isApplied = true;

            // Update settings
            _settings.LastWallpaperPath = filePath;
            _settings.Save();

            // Update UI
            UpdateFileInfo(filePath);
            UpdatePreview(filePath);
        }

        private void StopWallpaper()
        {
            _player.Stop();
            _desktop.Detach();
            _isApplied = false;
            _pausedByFullscreen = false;
            _pausedBySystem = false;

            ClearPreview();

            if (_playerWindow != null)
            {
                _playerWindow.PlayerGrid.Children.Clear();
                _playerWindow.Close();
                _playerWindow = null;
            }
        }

        private void UpdateFileInfo(string filePath)
        {
            FileNameText.Text = Path.GetFileName(filePath);

            try
            {
                var fi = new FileInfo(filePath);
                string size = fi.Length switch
                {
                    < 1024 => $"{fi.Length} B",
                    < 1048576 => $"{fi.Length / 1024.0:F1} KB",
                    < 1073741824 => $"{fi.Length / 1048576.0:F1} MB",
                    _ => $"{fi.Length / 1073741824.0:F2} GB"
                };
                FileInfoText.Text = $"{fi.Extension.TrimStart('.').ToUpperInvariant()} • {size}";
            }
            catch
            {
                FileInfoText.Text = "";
            }
        }

        private void UpdatePreview(string filePath)
        {
            try
            {
                PreviewMedia.Source = new Uri(filePath, UriKind.Absolute);
                PreviewMedia.IsMuted = true;
                PreviewMedia.Play();
                PreviewMedia.Visibility = Visibility.Visible;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch
            {
                ClearPreview();
            }
        }

        private void ClearPreview()
        {
            try
            {
                PreviewMedia.Stop();
                PreviewMedia.Source = null;
                PreviewMedia.Close();
            }
            catch { }

            PreviewMedia.Visibility = Visibility.Collapsed;
            PreviewPlaceholder.Visibility = Visibility.Visible;
        }

        #endregion

        #region Playback Controls

        private void OnPlayPause(object sender, RoutedEventArgs e)
        {
            if (_player.CurrentState == WallpaperPlayer.PlaybackState.Playing)
            {
                _player.Pause();
            }
            else if (_player.CurrentState == WallpaperPlayer.PlaybackState.Paused)
            {
                _player.Resume();
                _pausedByFullscreen = false;
            }
            else if (!string.IsNullOrEmpty(_settings.LastWallpaperPath) &&
                     File.Exists(_settings.LastWallpaperPath))
            {
                ApplyWallpaper(_settings.LastWallpaperPath);
            }
        }

        private void OnStop(object sender, RoutedEventArgs e)
        {
            StopWallpaper();
        }

        private void OnToggleMute(object sender, RoutedEventArgs e)
        {
            _player.ToggleMute();
            _settings.IsMuted = _player.IsMuted;
            _settings.Save();
            UpdateMuteButton();
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressEvents) return;
            _player.Volume = e.NewValue / 100.0;
            _settings.Volume = _player.Volume;
            _settings.Save();
        }

        #endregion

        #region Settings

        private void OnSettingChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;

            _settings.PauseOnFullscreen = PauseFullscreenToggle.IsChecked == true;
            _fullscreenDetector.IsEnabled = _settings.PauseOnFullscreen;
            _settings.Save();
        }

        private void OnStartupChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;

            if (StartWithWindowsToggle.IsChecked == true)
                StartupManager.Register();
            else
                StartupManager.Unregister();

            _settings.StartWithWindows = StartWithWindowsToggle.IsChecked == true;
            _settings.Save();
        }

        private void OnFpsChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            if (FpsComboBox.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag && int.TryParse(tag, out int fps))
            {
                _settings.TargetFps = fps;
                _player.TargetFps = fps;
                _settings.Save();
            }
        }

        #endregion

        #region Event Handlers

        private void OnPlayerStateChanged(WallpaperPlayer.PlaybackState state)
        {
            Dispatcher.BeginInvoke(() =>
            {
                switch (state)
                {
                    case WallpaperPlayer.PlaybackState.Playing:
                        PlayPauseButton.Content = "\uE769"; // Pause icon
                        PlayPauseButton.ToolTip = "Pause";
                        PlayPauseButton.IsEnabled = true;
                        StopButton.IsEnabled = true;
                        StatusText.Text = "Playing";
                        StatusText.Foreground = FindResource("SuccessBrush") as System.Windows.Media.Brush;
                        TrayPlayPauseItem.Header = "Pause";
                        TrayIcon.ToolTipText = $"Intonaco — Playing";
                        break;

                    case WallpaperPlayer.PlaybackState.Paused:
                        PlayPauseButton.Content = "\uE768"; // Play icon
                        PlayPauseButton.ToolTip = "Resume";
                        PlayPauseButton.IsEnabled = true;
                        StopButton.IsEnabled = true;
                        StatusText.Text = _pausedByFullscreen ? "Paused (fullscreen)" :
                                         _pausedBySystem ? "Paused (system)" : "Paused";
                        StatusText.Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
                        TrayPlayPauseItem.Header = "Resume";
                        TrayIcon.ToolTipText = "Intonaco — Paused";
                        break;

                    case WallpaperPlayer.PlaybackState.Stopped:
                        PlayPauseButton.Content = "\uE768"; // Play icon
                        PlayPauseButton.ToolTip = "Play";
                        PlayPauseButton.IsEnabled = !string.IsNullOrEmpty(_settings.LastWallpaperPath);
                        StopButton.IsEnabled = false;
                        StatusText.Text = "Stopped";
                        StatusText.Foreground = FindResource("TextSecondaryBrush") as System.Windows.Media.Brush;
                        TrayPlayPauseItem.Header = "Play";
                        TrayIcon.ToolTipText = "Intonaco";
                        // Keep the last file name visible: ApplyWallpaper stops the
                        // previous video first, and blanking the name here race-wins
                        // over UpdateFileInfo for the newly applied wallpaper.
                        break;

                    case WallpaperPlayer.PlaybackState.Error:
                        PlayPauseButton.Content = "\uE768";
                        PlayPauseButton.IsEnabled = !string.IsNullOrEmpty(_settings.LastWallpaperPath);
                        StopButton.IsEnabled = false;
                        StatusText.Text = "Error";
                        StatusText.Foreground = FindResource("ErrorBrush") as System.Windows.Media.Brush;
                        TrayIcon.ToolTipText = "Intonaco — Error";
                        break;
                }

                UpdateMuteButton();
            });
        }

        private void OnPlayerError(string message)
        {
            Dispatcher.BeginInvoke(() => ShowError(message));
        }

        private void OnMediaOpened(TimeSpan duration)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (duration > TimeSpan.Zero)
                {
                    string durationStr = duration.TotalHours >= 1
                        ? duration.ToString(@"h\:mm\:ss")
                        : duration.ToString(@"m\:ss");

                    if (!string.IsNullOrEmpty(FileInfoText.Text))
                        FileInfoText.Text += $" • {durationStr}";
                }
            });
        }

        private void OnFullscreenChanged(bool isFullscreen)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (isFullscreen && _player.CurrentState == WallpaperPlayer.PlaybackState.Playing)
                {
                    _pausedByFullscreen = true;
                    _player.Pause();
                }
                else if (!isFullscreen && _pausedByFullscreen)
                {
                    _pausedByFullscreen = false;
                    if (!_pausedBySystem)
                        _player.Resume();
                }
            });
        }

        private void OnSystemPause()
        {
            Dispatcher.BeginInvoke(() =>
            {
                _pausedBySystem = true;
                if (_player.CurrentState == WallpaperPlayer.PlaybackState.Playing)
                    _player.Pause();
            });
        }

        private void OnSystemResume()
        {
            Dispatcher.BeginInvoke(() =>
            {
                _pausedBySystem = false;
                if (!_pausedByFullscreen && _player.CurrentState == WallpaperPlayer.PlaybackState.Paused)
                {
                    // Small delay after wake to let display stabilize
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (_, _) =>
                    {
                        timer.Stop();
                        if (_isApplied)
                        {
                            EnsureAttached();
                            _player.Resume();
                        }
                    };
                    timer.Start();
                }
            });
        }

        private void OnDisplayChanged()
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_isApplied)
                {
                    _desktop.ResizeToDesktop();
                }
            });
        }

        /// <summary>
        /// Reattaches the player window to the (possibly recreated) WorkerW layer.
        /// When Explorer restarts, the old WorkerW — and any child window parented
        /// to it — is destroyed, so a plain reattach can fail; in that case the
        /// wallpaper is re-applied from scratch with a fresh player window.
        /// </summary>
        private void EnsureAttached()
        {
            if (_desktop.Reattach())
                return;

            string? path = _player.CurrentFilePath ?? _settings.LastWallpaperPath;
            var state = _player.CurrentState;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                ApplyWallpaper(path);
                if (state == WallpaperPlayer.PlaybackState.Paused)
                    _player.Pause();
            }
        }

        private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressEvents) return;

            if (ThemeComboBox.SelectedItem is ComboBoxItem item && item.Tag is string theme)
            {
                _settings.Theme = theme;
                _settings.Save();
                (Application.Current as App)?.ApplyTheme(theme);
            }
        }

        #endregion

        #region Window Management

        private void OnWindowStateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
            }
        }

        private void OnWindowClosing(object? sender, CancelEventArgs e)
        {
            if (!_isExiting)
            {
                // Minimize to tray instead of closing
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                return;
            }

            // Actual shutdown
            _fullscreenDetector.Dispose();
            _systemEvents.Dispose();
            StopWallpaper();
            _player.Dispose();
            _desktop.Dispose();
            TrayIcon.Dispose();
        }

        private void ShowMainWindow()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Activate();
            Focus();
        }

        #endregion

        #region System Tray

        private void OnTrayShow(object sender, RoutedEventArgs e) => ShowMainWindow();

        private void OnTrayPlayPause(object sender, RoutedEventArgs e) =>
            OnPlayPause(sender, e);

        private void OnTrayToggleMute(object sender, RoutedEventArgs e) =>
            OnToggleMute(sender, e);

        private void OnTrayExit(object sender, RoutedEventArgs e)
        {
            _isExiting = true;
            Close();
            Application.Current.Shutdown();
        }

        #endregion

        #region UI Helpers

        private void UpdateMuteButton()
        {
            if (_player.IsMuted)
            {
                MuteButton.Content = "\uE74F"; // Muted icon
                MuteButton.ToolTip = "Unmute";
                TrayMuteItem.Header = "Unmute";
            }
            else
            {
                MuteButton.Content = "\uE767"; // Speaker icon
                MuteButton.ToolTip = "Mute";
                TrayMuteItem.Header = "Mute";
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;

            // Auto-clear after 10 seconds
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (ErrorText.Text == message)
                    ErrorText.Text = "";
            };
            timer.Start();
        }

        private void ClearError()
        {
            ErrorText.Text = "";
        }

        #endregion
    }
}
