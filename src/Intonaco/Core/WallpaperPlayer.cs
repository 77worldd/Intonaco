using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Intonaco.Core
{
    /// <summary>
    /// Manages video wallpaper playback using WPF MediaElement
    /// backed by Windows Media Foundation for native hardware-accelerated decoding.
    /// </summary>
    public sealed class WallpaperPlayer : IDisposable
    {
        private MediaElement? _mediaElement;
        private Uri? _currentSource;
        private bool _disposed;
        private bool _isPaused;
        private bool _isMuted;
        private double _volume = 0.0; // Default muted for wallpaper
        private int _targetFps = 30;
        private Stretch _stretchMode = Stretch.UniformToFill;
        private double _playbackSpeed = 1.0;
        private bool _loopPlayback = true;

        public event Action<string>? ErrorOccurred;
        public event Action<PlaybackState>? StateChanged;
        public event Action<TimeSpan>? MediaOpened;

        public enum PlaybackState
        {
            Stopped,
            Playing,
            Paused,
            Error
        }

        public PlaybackState CurrentState { get; private set; } = PlaybackState.Stopped;
        public bool IsPaused => _isPaused;
        public bool IsMuted => _isMuted;
        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0.0, 1.0);
                if (_mediaElement != null)
                    _mediaElement.Volume = _volume;
            }
        }

        public int TargetFps
        {
            get => _targetFps;
            set => _targetFps = Math.Clamp(value, 1, 60);
        }

        public Stretch StretchMode
        {
            get => _stretchMode;
            set
            {
                _stretchMode = value;
                if (_mediaElement != null)
                    _mediaElement.Stretch = value;
            }
        }

        public double PlaybackSpeed
        {
            get => _playbackSpeed;
            set
            {
                _playbackSpeed = double.IsNaN(value) || double.IsInfinity(value)
                    ? 1.0
                    : Math.Clamp(value, 0.25, 2.0);
                if (_mediaElement != null)
                    _mediaElement.SpeedRatio = _playbackSpeed;
            }
        }

        public bool LoopPlayback
        {
            get => _loopPlayback;
            set => _loopPlayback = value;
        }

        public TimeSpan Duration { get; private set; }
        public int NaturalVideoWidth { get; private set; }
        public int NaturalVideoHeight { get; private set; }
        public string? CurrentFilePath => _currentSource?.LocalPath;

        /// <summary>
        /// Initializes the player and returns the MediaElement to be hosted in a window.
        /// </summary>
        public MediaElement Initialize()
        {
            // Detach handlers from a previous element (re-apply creates a new one).
            if (_mediaElement != null)
            {
                _mediaElement.MediaOpened -= OnMediaOpened;
                _mediaElement.MediaEnded -= OnMediaEnded;
                _mediaElement.MediaFailed -= OnMediaFailed;
            }

            _mediaElement = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                Stretch = _stretchMode,
                SpeedRatio = _playbackSpeed,
                Volume = _volume,
                IsMuted = true, // Wallpapers should be silent by default
                IsHitTestVisible = false
            };

            _mediaElement.MediaOpened += OnMediaOpened;
            _mediaElement.MediaEnded += OnMediaEnded;
            _mediaElement.MediaFailed += OnMediaFailed;

            return _mediaElement;
        }

        /// <summary>
        /// Loads and begins playing a video file as wallpaper.
        /// </summary>
        public bool Play(string filePath)
        {
            if (_disposed || _mediaElement == null) return false;

            if (!File.Exists(filePath))
            {
                ErrorOccurred?.Invoke($"File not found: {Path.GetFileName(filePath)}");
                SetState(PlaybackState.Error);
                return false;
            }

            try
            {
                Stop();

                _currentSource = new Uri(filePath, UriKind.Absolute);
                _mediaElement.Source = _currentSource;
                _mediaElement.Stretch = _stretchMode;
                _mediaElement.SpeedRatio = _playbackSpeed;
                _mediaElement.IsMuted = _isMuted;
                _mediaElement.Volume = _volume;
                _mediaElement.Play();
                _isPaused = false;
                SetState(PlaybackState.Playing);
                return true;
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Failed to play: {ex.Message}");
                SetState(PlaybackState.Error);
                return false;
            }
        }

        /// <summary>
        /// Pauses playback. Used during fullscreen apps or user-initiated pause.
        /// </summary>
        public void Pause()
        {
            if (_disposed || _mediaElement == null || CurrentState != PlaybackState.Playing) return;

            try
            {
                _mediaElement.Pause();
                _isPaused = true;
                SetState(PlaybackState.Paused);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Pause failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Resumes playback from paused state.
        /// </summary>
        public void Resume()
        {
            if (_disposed || _mediaElement == null || CurrentState != PlaybackState.Paused) return;

            try
            {
                // Resuming after a non-looping video ended should restart it
                // instead of leaving the final frame stuck at the end.
                if (Duration > TimeSpan.Zero &&
                    _mediaElement.Position >= Duration - TimeSpan.FromMilliseconds(250))
                {
                    _mediaElement.Position = TimeSpan.Zero;
                }
                _mediaElement.SpeedRatio = _playbackSpeed;
                _mediaElement.Play();
                _isPaused = false;
                SetState(PlaybackState.Playing);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Resume failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops playback and releases the media source.
        /// </summary>
        public void Stop()
        {
            if (_mediaElement == null) return;

            try
            {
                _mediaElement.Stop();
                _mediaElement.Source = null;
                _mediaElement.Close();
            }
            catch { /* Best effort cleanup */ }

            _currentSource = null;
            _isPaused = false;
            Duration = TimeSpan.Zero;
            NaturalVideoWidth = 0;
            NaturalVideoHeight = 0;
            SetState(PlaybackState.Stopped);
        }

        public void ToggleMute()
        {
            _isMuted = !_isMuted;
            if (_mediaElement != null)
                _mediaElement.IsMuted = _isMuted;
        }

        public void SetMute(bool muted)
        {
            _isMuted = muted;
            if (_mediaElement != null)
                _mediaElement.IsMuted = _isMuted;
        }

        private void OnMediaOpened(object? sender, RoutedEventArgs e)
        {
            if (_mediaElement == null) return;
            Duration = _mediaElement.NaturalDuration.HasTimeSpan
                ? _mediaElement.NaturalDuration.TimeSpan
                : TimeSpan.Zero;
            NaturalVideoWidth = _mediaElement.NaturalVideoWidth;
            NaturalVideoHeight = _mediaElement.NaturalVideoHeight;
            MediaOpened?.Invoke(Duration);
        }

        private void OnMediaEnded(object? sender, RoutedEventArgs e)
        {
            if (_mediaElement == null || _disposed || _isPaused)
                return;

            if (_loopPlayback)
            {
                _mediaElement.Position = TimeSpan.Zero;
                _mediaElement.SpeedRatio = _playbackSpeed;
                _mediaElement.Play();
                return;
            }

            // Keep the last frame on the desktop rather than replacing it
            // with black; Resume knows to rewind from the end.
            _isPaused = true;
            SetState(PlaybackState.Paused);
        }

        private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            string msg = e.ErrorException?.Message ?? "Unknown playback error";
            ErrorOccurred?.Invoke(msg);
            SetState(PlaybackState.Error);
        }

        private void SetState(PlaybackState state)
        {
            if (CurrentState != state)
            {
                CurrentState = state;
                StateChanged?.Invoke(state);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Stop();

            if (_mediaElement != null)
            {
                _mediaElement.MediaOpened -= OnMediaOpened;
                _mediaElement.MediaEnded -= OnMediaEnded;
                _mediaElement.MediaFailed -= OnMediaFailed;
                _mediaElement = null;
            }
        }
    }
}
