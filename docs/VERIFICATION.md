# Verification checklist

Intonaco is built and unit-tested on Windows by GitHub Actions on every push
(see `.github/workflows/ci.yml`). The checks below **require an interactive
Windows desktop session** and therefore cannot run on a CI agent. They are
tracked here — and in the
[verification issue](https://github.com/77worldd/Intonaco/issues) — so they
are never silently skipped. If you run any of them, please report results
(with Windows build, GPU, and video details) on that issue or via PR.

## Automated (CI, every push)

- [x] `dotnet restore` + `dotnet build -c Release` succeeds on `windows-latest`
- [x] `dotnet test -c Release` — settings normalization/recents, player
      playback options, fullscreen-detector and frame-rate-limiter tests pass
- [x] Framework-dependent and self-contained single-file publishes succeed

## Manual (interactive Windows 10/11 session)

### Desktop integration

- [ ] Apply an H.264 MP4: the video renders behind desktop icons, and icons
      remain visible, selectable and double-clickable
- [ ] On Windows 11 24H2+/26H2, the fresh `%LocalAppData%\Intonaco\debug.log`
      shows the `0x052C (0xD, 1)` split request when no WorkerW layer existed,
      the Worker's appearance, `verified=True` with alpha 255, and Progman
      children ordered `SHELLDLL_DefView` → player → system WorkerW
- [ ] Desktop right-click menu (on empty wallpaper area) still works
- [ ] Win+D (Show desktop) keeps the wallpaper playing
- [ ] Second monitor / mixed-DPI setup: note which monitors are covered
      (WorkerW normally spans the primary monitor)

### Playback

- [ ] 1080p and 4K H.264 files loop seamlessly with no black flash
- [ ] HEVC file plays when the HEVC Video Extension is installed
- [ ] Play/Pause/Stop/Mute/volume from the main window and from the tray menu
- [ ] Preview in the main window stays muted and stops on Stop
- [ ] Dropping a supported video onto the window applies it; dropping a
      non-video shows a friendly error
- [ ] Applying a video adds it to Recent wallpapers, and a recent item reapplies
      it in one click
- [ ] Fill/Fit/Stretch changes both the preview and actual desktop immediately
- [ ] Playback speed and Loop playback changes are honored by preview and
      desktop playback
- [ ] Moving the volume slider while muted unmutes and updates the percentage

### v1.2.0 native interface and branding

- [ ] The new cat/image mark appears in the EXE properties, taskbar button,
      window title bar, system tray and app header; it remains recognizable
      at 100%, 125% and 150% display scaling
- [ ] The Playing on desktop/Paused/Ready/No wallpaper header status matches
      the actual desktop state
- [ ] Volume can be dragged smoothly, clicked to a position and adjusted by
      keyboard; the percentage and mute state stay in sync
- [ ] Dark, Light and System themes render readable Settings-style cards,
      controls and ComboBox popups; the title-bar caption follows the app
      surface and selected theme
- [ ] The scrollable window remains usable at its minimum size and common
      Windows display-scaling values (100%, 125%, 150%)
- [ ] Open location reveals the current video in File Explorer, and Open logs
      opens `%LocalAppData%\Intonaco`

### Power / shell events

- [ ] Lock (Win+L) pauses; unlock resumes
- [ ] Sleep pauses; wake reattaches (wallpaper visible again within ~3 s)
- [ ] Kill `explorer.exe` in Task Manager and restart it: the wallpaper
      reattaches automatically (`TaskbarCreated` handling)
- [ ] Resolution change / monitor disconnect re-fits the wallpaper

### Fullscreen detection

- [ ] Launching a fullscreen game pauses the wallpaper within ~2 s
- [ ] Exiting the game resumes it
- [ ] A merely *maximized* browser window does **not** pause playback

### Performance measurement

Record Task Manager / Performance Monitor values during 1080p H.264 playback
and compare with the estimates in `README.md`:

- [ ] Private working set (RAM) while playing / paused / idle in tray
- [ ] Average CPU % and GPU (Video Decode engine) % while playing
- [ ] Same metrics while a fullscreen app is active (expect ~0%)

### Release hygiene

- [ ] Replace `docs/screenshot-placeholder.png` with a real screenshot of the
      main window (and ideally the live desktop)
- [ ] Confirm the published zips run from a clean folder on a machine
      without the .NET SDK (self-contained) / with only the runtime
      (framework-dependent)

## Known limitations

- WPF `MediaElement` renders at the video's native frame rate. The FPS
  preference is persisted, but a hard per-frame cap is not enforced on the
  `MediaElement` path; true throttling would need a Media Foundation
  pipeline with manual presentation. Tracked as future work.
- Format support is bounded by installed Windows codecs (HEVC needs the
  HEVC Video Extensions).
- The WorkerW technique relies on the undocumented `0x052C` Progman message.
  It has been stable since Windows 7, but Microsoft could change it in a
  future release.
