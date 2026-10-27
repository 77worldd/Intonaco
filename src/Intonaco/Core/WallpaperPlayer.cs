using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Intonaco.Core
{
    /// <summary>
    /// Manages video wallpaper playback using WPF MediaElement backed by
    /// Windows Media Foundation for native hardware-accelerated decoding.
    ///
    /// Pausing is deliberately stronger than MediaElement.Pause(): the
    /// currently displayed frame is captured into one static bitmap, the
    /// Media Foundation source is closed, and the decoder and its frame
    /// queues are released. Resuming reopens the file, seeks back to the
    /// paused position, and removes the static image only after WPF has had
    /// an opportunity to present decoded frames again.
    /// </summary>
    public sealed class WallpaperPlayer : IDisposable
    {
        private const int MaxPausedFrameWidth = 3840;
        private const int MaxPausedFrameHeight = 2160;
        private const long MaxPausedFramePixels = 3840L * 2160L;

        private enum PendingMediaAction
        {
            None,
            Resume,
            RefreshPausedFrame
        }

        private MediaElement? _mediaElement;
        private Grid? _host;
        private Image? _pausedImage;
        private BitmapSource? _pausedFrame;
        private Uri? _currentSource;
        private DispatcherTimer? _pausedFrameRefreshTimer;
        private EventHandler? _hidePausedFrameHandler;
        private PendingMediaAction _pendingMediaAction = PendingMediaAction.None;
        private TimeSpan _pendingPosition;
        private TimeSpan _pausedPosition;
        private bool _isMediaOpened;
        private bool _disposed;
        private bool _isPaused;
        private bool _isMuted;
        private double _volume = 0.0; // Default muted for wallpaper
        private int _targetFps = 30;
        private Stretch _stretchMode = Stretch.UniformToFill;
        private double _playbackSpeed = 1.0;
        private bool _loopPlayback = true;

        // Frame-rate throttling. Timeline.DesiredFrameRate caps the media
        // clock's presentation interval (30 fps -> ~33.3 ms instead of
        // ~16.6 ms), which is what actually lowers composition cost; the
        // CompositionTarget gate below measures the achieved rate so the
        // setting can be verified in debug.log instead of assumed.
        private readonly FrameRateLimiter _fpsLimiter = new(30);
        private EventHandler? _fpsRenderHandler;
        private bool _fpsThrottleActive;
        private DateTime _fpsGateLastLog = DateTime.MinValue;

        public event Action<string>? ErrorOccurred;
        public event Action<PlaybackState>? StateChanged;
        public event Action<TimeSpan>? MediaOpened;
        public event Action<BitmapSource?>? PausedFrameChanged;

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

        /// <summary>
        /// True when Pause has fully closed the Media Foundation pipeline and
        /// the desktop is being presented from <see cref="PausedFrame"/>.
        /// </summary>
        public bool IsSuspended { get; private set; }

        public BitmapSource? PausedFrame => _pausedFrame;

        internal bool HasActiveMediaPipeline => _mediaElement?.Source != null;

        public double Volume
        {
            get => _volume;
            set
            {
                _volume = Math.Clamp(value, 0.0, 1.0);
                if (_mediaElement != null && _pendingMediaAction != PendingMediaAction.RefreshPausedFrame)
                    _mediaElement.Volume = _volume;
            }
        }

        public int TargetFps
        {
            get => _targetFps;
            set
            {
                _targetFps = Math.Clamp(value, 1, 60);
                _fpsLimiter.TargetFps = _targetFps;

                // A live frame-rate change must reach the media clock
                // immediately, not only on the next ApplyMediaSettings.
                ApplyFrameRateCap();
                UpdateFrameRateThrottle();
            }
        }

        public Stretch StretchMode
        {
            get => _stretchMode;
            set
            {
                bool changed = _stretchMode != value;
                _stretchMode = value;
                if (_mediaElement != null)
                    _mediaElement.Stretch = value;

                // A suspended frame has the previous fit baked into the
                // desktop-sized capture. Reopen briefly behind that image,
                // recapture with the new fit, then close the decoder again.
                if (changed && IsSuspended && CurrentState == PlaybackState.Paused)
                    RefreshPausedFrame();
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

        public TimeSpan Position
        {
            get
            {
                if (IsSuspended || _pendingMediaAction == PendingMediaAction.RefreshPausedFrame)
                    return _pausedPosition;

                try
                {
                    return _mediaElement?.Position ?? _pausedPosition;
                }
                catch
                {
                    return _pausedPosition;
                }
            }
        }

        /// <summary>
        /// Initializes the player and returns the MediaElement. Desktop code
        /// should prefer <see cref="CreateHost"/>, which also creates the
        /// static paused-frame layer above the video.
        /// </summary>
        public MediaElement Initialize()
        {
            CancelPendingFrameWork();
            ReleaseMediaElement(_mediaElement);
            _mediaElement = null;
            _host = null;
            _pausedImage = null;
            ClearPausedFrame();
            _pendingMediaAction = PendingMediaAction.None;
            _isMediaOpened = false;
            IsSuspended = false;
            _pausedPosition = TimeSpan.Zero;

            _mediaElement = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Manual,
                ScrubbingEnabled = true,
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
        /// Creates the desktop host: the live MediaElement below a static
        /// Image used only while the media pipeline is suspended.
        /// </summary>
        public FrameworkElement CreateHost()
        {
            var mediaElement = Initialize();

            _pausedImage = new Image
            {
                // The capture is already rendered at the desktop host's
                // layout size with StretchMode applied by MediaElement.
                Stretch = Stretch.Fill,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false
            };
            RenderOptions.SetBitmapScalingMode(
                _pausedImage, BitmapScalingMode.HighQuality);

            _host = new Grid
            {
                Background = Brushes.Black,
                ClipToBounds = true,
                IsHitTestVisible = false
            };
            _host.Children.Add(mediaElement);
            _host.Children.Add(_pausedImage);

            // The host root carries the frame-rate cap too, so the value is
            // still in scope if the MediaElement is re-created on resume.
            ApplyFrameRateCap();

            return _host;
        }

        /// <summary>
        /// Loads and begins playing a video file as wallpaper.
        /// </summary>
        public bool Play(string filePath)
        {
            if (_disposed || _mediaElement == null) return false;

            // Reject unreadable/unsupported files before Media Foundation is
            // involved: opening an undecodable container used to leave the
            // desktop black with no explanation.
            if (!MediaFormatProbe.TryValidate(filePath, out string probeError))
            {
                DebugLog.Write($"Play rejected '{filePath}': {probeError}");
                ErrorOccurred?.Invoke(probeError);
                SetState(PlaybackState.Error);
                return false;
            }

            try
            {
                Stop();

                _currentSource = new Uri(filePath, UriKind.Absolute);
                _isMediaOpened = false;
                _pendingMediaAction = PendingMediaAction.None;
                _mediaElement.Visibility = Visibility.Visible;
                _mediaElement.Source = _currentSource;
                ApplyMediaSettings();
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
        /// Pauses playback and, when a frame can be captured, closes the
        /// decoder so a paused wallpaper costs like a static image. If frame
        /// capture is unavailable, this safely falls back to a native
        /// MediaElement pause instead of replacing the desktop with black.
        /// </summary>
        public void Pause()
        {
            if (_disposed || _mediaElement == null || CurrentState != PlaybackState.Playing) return;

            try
            {
                _mediaElement.Pause();
                _pausedPosition = SafeCurrentPosition();
                _isPaused = true;

                BitmapSource? frame = CaptureCurrentFrame();
                if (frame != null)
                {
                    SetPausedFrame(frame);
                    SuspendMediaPipeline();
                    DebugLog.Write(
                        $"Wallpaper suspended at {_pausedPosition}; " +
                        $"static frame {frame.PixelWidth}x{frame.PixelHeight}.");
                }
                else if (!_isMediaOpened)
                {
                    // There is no decoded frame to preserve yet. Closing the
                    // half-open source still avoids retaining the pipeline.
                    ClearPausedFrame();
                    SuspendMediaPipeline();
                    DebugLog.Write("Wallpaper suspended before its first frame.");
                }
                else
                {
                    IsSuspended = false;
                    DebugLog.Write(
                        "Paused-frame capture was unavailable; using native " +
                        "MediaElement pause as a visual fallback.");
                }

                SetState(PlaybackState.Paused);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Pause failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Suspends at an already-known position/frame. Explorer-restart
        /// recovery uses this to preserve the exact paused desktop while it
        /// creates a new player window.
        /// </summary>
        internal void PauseAt(TimeSpan position, BitmapSource? frame)
        {
            if (_disposed || _mediaElement == null ||
                (CurrentState != PlaybackState.Playing && CurrentState != PlaybackState.Paused))
            {
                return;
            }

            try
            {
                _mediaElement.Pause();
                _pausedPosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
                _isPaused = true;

                if (frame != null)
                    SetPausedFrame(frame);
                else
                    ClearPausedFrame();

                SuspendMediaPipeline();
                SetState(PlaybackState.Paused);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Pause failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Resumes playback, reopening and seeking the media source first if
        /// Pause had suspended it.
        /// </summary>
        public void Resume()
        {
            if (_disposed || _mediaElement == null || CurrentState != PlaybackState.Paused) return;

            try
            {
                if (_pendingMediaAction == PendingMediaAction.RefreshPausedFrame)
                {
                    // A fit-change recapture already reopened the source.
                    // Convert it into a normal resume without another open.
                    _pausedFrameRefreshTimer?.Stop();
                    _pendingMediaAction = PendingMediaAction.Resume;
                    _pendingPosition = _pausedPosition;
                    _isPaused = false;
                    ApplyMediaSettings();
                    SetState(PlaybackState.Playing);

                    if (_isMediaOpened)
                    {
                        _pendingMediaAction = PendingMediaAction.None;
                        _mediaElement.Play();
                        HidePausedFrameAfterRender();
                    }
                    return;
                }

                if (!IsSuspended)
                {
                    // Native-pause fallback: the source is still open.
                    if (Duration > TimeSpan.Zero &&
                        _mediaElement.Position >= Duration - TimeSpan.FromMilliseconds(250))
                    {
                        _mediaElement.Position = TimeSpan.Zero;
                    }
                    ApplyMediaSettings();
                    _mediaElement.Play();
                    _isPaused = false;
                    ClearPausedFrame();
                    SetState(PlaybackState.Playing);
                    return;
                }

                if (_currentSource == null)
                    return;

                string path = _currentSource.LocalPath;
                if (!File.Exists(path))
                {
                    ErrorOccurred?.Invoke($"File not found: {Path.GetFileName(path)}");
                    SetState(PlaybackState.Error);
                    return;
                }

                _pendingPosition = _pausedPosition;
                if (Duration > TimeSpan.Zero &&
                    _pendingPosition >= Duration - TimeSpan.FromMilliseconds(250))
                {
                    _pendingPosition = TimeSpan.Zero;
                }

                _pendingMediaAction = PendingMediaAction.Resume;
                IsSuspended = false;
                _isMediaOpened = false;
                _mediaElement.Visibility = Visibility.Visible;
                _mediaElement.Source = _currentSource;
                ApplyMediaSettings();

                // Start the clock now; MediaOpened performs the exact seek
                // before the paused image is removed, so no wrong frame or
                // black flash is presented.
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
        /// Recaptures the suspended frame after a layout-affecting change
        /// (fit mode or display size), then closes the decoder again. The
        /// old static frame stays visible throughout.
        /// </summary>
        public void RefreshPausedFrame()
        {
            if (_disposed || _mediaElement == null || _currentSource == null ||
                CurrentState != PlaybackState.Paused || !IsSuspended ||
                _pendingMediaAction != PendingMediaAction.None)
            {
                return;
            }

            try
            {
                _pendingPosition = _pausedPosition;
                _pendingMediaAction = PendingMediaAction.RefreshPausedFrame;
                IsSuspended = false;
                _isMediaOpened = false;
                _mediaElement.Visibility = Visibility.Visible;
                _mediaElement.Source = _currentSource;
                ApplyMediaSettings();

                // Never make a silent recapture audible.
                _mediaElement.IsMuted = true;
                _mediaElement.Volume = 0;
            }
            catch (Exception ex)
            {
                _pendingMediaAction = PendingMediaAction.None;
                IsSuspended = true;
                DebugLog.Write($"Paused-frame refresh failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops playback and releases the media source and paused frame.
        /// </summary>
        public void Stop()
        {
            CancelPendingFrameWork();
            _pendingMediaAction = PendingMediaAction.None;

            if (_mediaElement != null)
            {
                ReleaseMediaPipeline();
                _mediaElement.Visibility = Visibility.Visible;
            }

            _currentSource = null;
            _isPaused = false;
            IsSuspended = false;
            _isMediaOpened = false;
            _pausedPosition = TimeSpan.Zero;
            _pendingPosition = TimeSpan.Zero;
            ClearPausedFrame();
            Duration = TimeSpan.Zero;
            NaturalVideoWidth = 0;
            NaturalVideoHeight = 0;
            SetState(PlaybackState.Stopped);
        }

        public void ToggleMute()
        {
            _isMuted = !_isMuted;
            if (_mediaElement != null && _pendingMediaAction != PendingMediaAction.RefreshPausedFrame)
                _mediaElement.IsMuted = _isMuted;
        }

        public void SetMute(bool muted)
        {
            _isMuted = muted;
            if (_mediaElement != null && _pendingMediaAction != PendingMediaAction.RefreshPausedFrame)
                _mediaElement.IsMuted = _isMuted;
        }

        internal static double CalculatePausedFrameScale(int width, int height)
        {
            if (width <= 0 || height <= 0)
                return 1.0;

            double scale = Math.Min(
                (double)MaxPausedFrameWidth / width,
                (double)MaxPausedFrameHeight / height);
            long pixels = (long)width * height;
            if (pixels > MaxPausedFramePixels)
                scale = Math.Min(scale, Math.Sqrt((double)MaxPausedFramePixels / pixels));

            return Math.Min(1.0, scale);
        }

        internal static BitmapSource? CaptureFrame(MediaElement mediaElement)
        {
            try
            {
                if (mediaElement.Source == null)
                    return null;

                mediaElement.UpdateLayout();
                int width = (int)Math.Ceiling(mediaElement.ActualWidth);
                int height = (int)Math.Ceiling(mediaElement.ActualHeight);
                if (width < 2 || height < 2)
                    return null;

                var bitmap = new RenderTargetBitmap(
                    width, height, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(mediaElement);

                BitmapSource result = bitmap;
                double scale = CalculatePausedFrameScale(width, height);
                if (scale < 0.999)
                {
                    var transformed = new TransformedBitmap(
                        bitmap, new ScaleTransform(scale, scale));

                    // Materialize the downscaled pixels. Keeping only the
                    // TransformedBitmap would retain the full-size source.
                    result = new WriteableBitmap(transformed);
                }

                if (result.CanFreeze)
                    result.Freeze();

                return result;
            }
            catch
            {
                return null;
            }
        }

        private BitmapSource? CaptureCurrentFrame()
        {
            if (_mediaElement == null || !_isMediaOpened ||
                NaturalVideoWidth <= 0 || NaturalVideoHeight <= 0)
            {
                return null;
            }

            return CaptureFrame(_mediaElement);
        }

        private void OnMediaOpened(object? sender, RoutedEventArgs e)
        {
            if (_mediaElement == null) return;

            _isMediaOpened = true;
            Duration = _mediaElement.NaturalDuration.HasTimeSpan
                ? _mediaElement.NaturalDuration.TimeSpan
                : TimeSpan.Zero;
            NaturalVideoWidth = _mediaElement.NaturalVideoWidth;
            NaturalVideoHeight = _mediaElement.NaturalVideoHeight;

            PendingMediaAction action = _pendingMediaAction;
            _pendingMediaAction = PendingMediaAction.None;

            if (action != PendingMediaAction.None)
            {
                try
                {
                    TimeSpan target = _pendingPosition;
                    if (Duration > TimeSpan.Zero)
                    {
                        if (target >= Duration - TimeSpan.FromMilliseconds(250))
                            target = TimeSpan.Zero;
                        else if (target > Duration)
                            target = Duration;
                    }

                    if (target > TimeSpan.Zero)
                        _mediaElement.Position = target;
                    else if (action == PendingMediaAction.Resume && _pausedPosition > TimeSpan.Zero)
                        _mediaElement.Position = TimeSpan.Zero;

                    ApplyMediaSettings();

                    if (action == PendingMediaAction.Resume)
                    {
                        _mediaElement.Play();
                        HidePausedFrameAfterRender();
                    }
                    else
                    {
                        _mediaElement.IsMuted = true;
                        _mediaElement.Volume = 0;
                        _mediaElement.Play();
                        StartPausedFrameRefreshTimer();

                        // OnMediaOpened cleared the field above so user
                        // actions can safely supersede the recapture; mark
                        // the short timer phase as pending again.
                        _pendingMediaAction = PendingMediaAction.RefreshPausedFrame;
                    }
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"Failed to restore playback position: {ex.Message}");
                }
            }

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

            // Capture and suspend the final frame rather than retaining an
            // ended decoder indefinitely; Resume rewinds from the end.
            Pause();
        }

        private void OnMediaFailed(object? sender, ExceptionRoutedEventArgs e)
        {
            string raw = e.ErrorException?.Message ?? "Unknown playback error";
            string msg = BuildUserFriendlyError(raw);

            if (_pendingMediaAction == PendingMediaAction.RefreshPausedFrame)
            {
                // The previous static frame is still valid. Release the
                // failed recapture attempt without surfacing a new error.
                _pausedFrameRefreshTimer?.Stop();
                _pendingMediaAction = PendingMediaAction.None;
                ReleaseMediaPipeline();
                IsSuspended = true;
                _isMediaOpened = false;
                if (_mediaElement != null)
                    _mediaElement.Visibility = Visibility.Collapsed;
                DebugLog.Write($"Paused-frame refresh media failed: {raw}");
                return;
            }

            if (_pendingMediaAction == PendingMediaAction.Resume)
            {
                _pendingMediaAction = PendingMediaAction.None;
                ReleaseMediaPipeline();
                IsSuspended = true;
                _isPaused = true;
                _isMediaOpened = false;
                if (_mediaElement != null)
                    _mediaElement.Visibility = Visibility.Collapsed;
            }

            DebugLog.Write($"Media failed: {raw} -> {msg}");
            ErrorOccurred?.Invoke(msg);
            SetState(PlaybackState.Error);
        }

        private static string BuildUserFriendlyError(string rawMessage)
        {
            // Media Foundation errors often contain HRESULTs; map to a friendly
            // codec hint so users aren't left staring at a black screen.
            string lower = rawMessage.ToLowerInvariant();
            bool isCodecError = lower.Contains("0xc00d36b4") // MF_E_INVALIDMEDIATYPE
                             || lower.Contains("0xc00d36c4")
                             || lower.Contains("0xc00d5212")
                             || lower.Contains("unsupported")
                             || lower.Contains("could not be decoded")
                             || lower.Contains("mf_media_engine_err_src_not_supported")
                             || lower.Contains("failed to decode");

            if (isCodecError)
            {
                return "Unable to play video: Unsupported codec or format. Please use standard MP4 (H.264) or WebM files.";
            }

            // Wrap generic failures with the same friendly prefix so toast is actionable.
            if (lower.Contains("not found") || lower.Contains("cannot find"))
                return rawMessage;

            return rawMessage.Contains("Unsupported", StringComparison.OrdinalIgnoreCase)
                ? rawMessage
                : $"Unable to play video: {rawMessage} Try converting to MP4 (H.264).";
        }

        private void StartPausedFrameRefreshTimer()
        {
            _pausedFrameRefreshTimer ??= new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(200)
            };
            _pausedFrameRefreshTimer.Tick -= OnPausedFrameRefreshTimerTick;
            _pausedFrameRefreshTimer.Tick += OnPausedFrameRefreshTimerTick;
            _pausedFrameRefreshTimer.Stop();
            _pausedFrameRefreshTimer.Start();
        }

        private void OnPausedFrameRefreshTimerTick(object? sender, EventArgs e)
        {
            _pausedFrameRefreshTimer?.Stop();

            if (_disposed || _mediaElement == null ||
                CurrentState != PlaybackState.Paused ||
                _pendingMediaAction != PendingMediaAction.RefreshPausedFrame)
            {
                return;
            }

            _pendingMediaAction = PendingMediaAction.None;

            try
            {
                _mediaElement.Pause();
                _pausedPosition = SafeCurrentPosition();
                BitmapSource? frame = CaptureCurrentFrame();
                if (frame != null)
                    SetPausedFrame(frame);
            }
            catch (Exception ex)
            {
                DebugLog.Write($"Paused-frame recapture failed: {ex.Message}");
            }
            finally
            {
                SuspendMediaPipeline();
                ApplyMediaSettings();
            }
        }

        private void HidePausedFrameAfterRender()
        {
            CancelHidePausedFrame();
            int frames = 0;

            _hidePausedFrameHandler = (_, _) =>
            {
                frames++;
                if (frames < 3)
                    return;

                CancelHidePausedFrame();
                if (!_disposed && CurrentState == PlaybackState.Playing)
                    ClearPausedFrame();
            };

            CompositionTarget.Rendering += _hidePausedFrameHandler;
        }

        private void CancelHidePausedFrame()
        {
            if (_hidePausedFrameHandler == null)
                return;

            CompositionTarget.Rendering -= _hidePausedFrameHandler;
            _hidePausedFrameHandler = null;
        }

        private void CancelPendingFrameWork()
        {
            _pausedFrameRefreshTimer?.Stop();
            CancelHidePausedFrame();
            StopFrameRateThrottle();
        }

        private void SetPausedFrame(BitmapSource frame)
        {
            _pausedFrame = frame;
            if (_pausedImage != null)
            {
                _pausedImage.Source = frame;
                _pausedImage.Visibility = Visibility.Visible;
            }
            PausedFrameChanged?.Invoke(frame);
        }

        private void ClearPausedFrame()
        {
            bool hadFrame = _pausedFrame != null;
            _pausedFrame = null;

            if (_pausedImage != null)
            {
                _pausedImage.Source = null;
                _pausedImage.Visibility = Visibility.Collapsed;
            }

            if (_mediaElement != null && !IsSuspended)
                _mediaElement.Visibility = Visibility.Visible;

            if (hadFrame)
                PausedFrameChanged?.Invoke(null);
        }

        private void SuspendMediaPipeline()
        {
            StopFrameRateThrottle();
            ReleaseMediaPipeline();
            IsSuspended = true;
            _isMediaOpened = false;
            _pendingMediaAction = PendingMediaAction.None;

            if (_mediaElement != null)
                _mediaElement.Visibility = Visibility.Collapsed;
        }

        private void ReleaseMediaPipeline()
        {
            if (_mediaElement == null)
                return;

            try
            {
                _mediaElement.Stop();
                _mediaElement.Source = null;
                _mediaElement.Close();
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private void ReleaseMediaElement(MediaElement? element)
        {
            if (element == null)
                return;

            element.MediaOpened -= OnMediaOpened;
            element.MediaEnded -= OnMediaEnded;
            element.MediaFailed -= OnMediaFailed;

            try
            {
                element.Stop();
                element.Source = null;
                element.Close();
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private void ApplyMediaSettings()
        {
            if (_mediaElement == null)
                return;

            _mediaElement.Stretch = _stretchMode;
            _mediaElement.SpeedRatio = _playbackSpeed;
            _mediaElement.IsMuted = _isMuted;
            _mediaElement.Volume = _volume;

            ApplyFrameRateCap();
            UpdateFrameRateThrottle();
        }

        /// <summary>
        /// Caps the media timeline's presentation rate. 60 fps clears the cap
        /// (WPF default), any lower value sets Timeline.DesiredFrameRate, which
        /// is the frame-interval hint the WPF media clock honours: 30 fps means
        /// a ~33.3 ms interval instead of ~16.6 ms, halving the number of
        /// video frames composited onto the desktop. Applied to both the
        /// MediaElement and its host root so the value survives re-parenting.
        /// </summary>
        private void ApplyFrameRateCap()
        {
            int fps = Math.Clamp(_targetFps, 1, 60);

            try
            {
                // 60 fps means "uncapped": clear the attached value so WPF uses
                // its default. Anything lower writes the cap explicitly.
                if (fps >= 60)
                {
                    _mediaElement?.ClearValue(Timeline.DesiredFrameRateProperty);
                    _host?.ClearValue(Timeline.DesiredFrameRateProperty);
                }
                else
                {
                    _mediaElement?.SetValue(Timeline.DesiredFrameRateProperty, fps);
                    _host?.SetValue(Timeline.DesiredFrameRateProperty, fps);
                }
            }
            catch (Exception ex)
            {
                DebugLog.Write($"Frame-rate cap could not be applied: {ex.Message}");
            }
        }

        /// <summary>
        /// Runs the frame-interval gate and its measurement only while a
        /// wallpaper is actually animating below 60 fps, and tears it down the
        /// moment playback pauses, suspends or stops — a paused wallpaper must
        /// not keep a CompositionTarget.Rendering subscription alive.
        /// </summary>
        private void UpdateFrameRateThrottle()
        {
            if (_disposed) return;

            bool shouldThrottle =
                CurrentState == PlaybackState.Playing &&
                !IsSuspended &&
                _targetFps < 60 &&
                _mediaElement?.Source != null;

            if (!shouldThrottle)
            {
                StopFrameRateThrottle();
                return;
            }

            _fpsLimiter.TargetFps = _targetFps;

            if (_fpsThrottleActive)
                return;

            _fpsLimiter.Reset();
            _fpsGateLastLog = DateTime.MinValue;
            _fpsRenderHandler = OnFrameRateRender;
            CompositionTarget.Rendering += _fpsRenderHandler;
            _fpsThrottleActive = true;
            DebugLog.Write(
                $"Frame-rate gate engaged: target {_targetFps} fps " +
                $"(interval {_fpsLimiter.FrameInterval.TotalMilliseconds:F1} ms).");
        }

        private void StopFrameRateThrottle()
        {
            if (_fpsRenderHandler != null)
            {
                CompositionTarget.Rendering -= _fpsRenderHandler;
                _fpsRenderHandler = null;
            }

            if (_fpsThrottleActive)
            {
                double measured = _fpsLimiter.MeasuredFps;
                if (measured > 0)
                {
                    DebugLog.Write(
                        $"Frame-rate gate released: target {_targetFps} fps, " +
                        $"measured presentation {measured:F1} fps.");
                }
            }

            _fpsThrottleActive = false;
        }

        /// <summary>
        /// Presentation rate actually achieved by the frame gate (0 when the
        /// gate is not running, e.g. at 60 fps or while paused).
        /// </summary>
        public double MeasuredFps => _fpsLimiter.MeasuredFps;

        private void OnFrameRateRender(object? sender, EventArgs e)
        {
            if (_disposed || _mediaElement == null ||
                CurrentState != PlaybackState.Playing || IsSuspended)
            {
                StopFrameRateThrottle();
                return;
            }

            if (e is not RenderingEventArgs args)
                return;

            // Gate this composition tick against the configured frame interval
            // and keep the measured rate up to date. Ticks the gate rejects are
            // the ones Timeline.DesiredFrameRate already skipped presenting, so
            // the count below is what users can compare against Task Manager.
            _fpsLimiter.ShouldRender(args);

            // Re-assert the cap occasionally: WPF recreates the media clock on
            // seek/reopen, which can drop the attached value.
            DateTime now = DateTime.UtcNow;
            if (now - _fpsGateLastLog >= TimeSpan.FromSeconds(30))
            {
                _fpsGateLastLog = now;
                ApplyFrameRateCap();
                DebugLog.Write(
                    $"Frame-rate gate: target {_targetFps} fps, " +
                    $"measured presentation {_fpsLimiter.MeasuredFps:F1} fps.");
            }
        }

        private TimeSpan SafeCurrentPosition()
        {
            try
            {
                return _mediaElement?.Position ?? TimeSpan.Zero;
            }
            catch
            {
                return _pausedPosition;
            }
        }

        private void SetState(PlaybackState state)
        {
            if (CurrentState != state)
            {
                CurrentState = state;
                UpdateFrameRateThrottle();
                StateChanged?.Invoke(state);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            StopFrameRateThrottle();
            _fpsLimiter.Dispose();

            Stop();
            ReleaseMediaElement(_mediaElement);
            _mediaElement = null;
            _pausedImage = null;
            _host = null;
        }
    }
}
