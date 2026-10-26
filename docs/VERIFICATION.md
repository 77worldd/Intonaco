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
- [x] Hosted media-lifecycle test: a generated video opens, Pause
      captures a frame and reports no active media pipeline, Resume
      reopens it, and Stop clears both source and paused frame
- [x] Framework-dependent, self-contained single-file and installer
      ReadyToRun publishes succeed
- [x] Inno Setup builds, then silently installs to a test folder,
      creates Start Menu/desktop shortcuts, launches the installed app,
      reinstalls over it, uninstalls it, removes the test Run value and
      shortcuts, and leaves a sentinel in `%LocalAppData%\Intonaco`
      untouched

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
- [ ] Pause leaves the current real video frame (not black) as a static
      desktop image; `debug.log` says `Wallpaper suspended` with the
      captured frame dimensions, and Resume continues near the paused
      position without a black/first-frame flash
- [ ] Repeat Pause → Resume at least 20 times and switch wallpapers
      several times: no crash, stuck black frame, steadily growing
      private memory or handle count
- [ ] Change Fill/Fit/Stretch while paused: the static frame updates
      behind the old image without becoming a live video again
- [ ] Preview in the main window stays muted and stops on Stop; while
      paused it shows a static frame rather than continuing to animate
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

### v1.3.0 installer

- [ ] Run `Intonaco-v1.3.0-win-x64-setup.exe` as a standard user: setup
      completes without an administrator prompt, creates the Start Menu
      shortcut, respects the optional desktop-shortcut choice, and the
      post-install launch option opens the current interface
- [ ] Installed app path, icon, version and uninstall entry are correct
      in File Explorer and Windows Settings → Apps
- [ ] Apply a wallpaper with the installed build; minimize to tray,
      pause/resume from the tray, enable Start with Windows, sign out/in
      if practical, and confirm the installed path still launches
- [ ] Run the same installer again over the existing installation:
      settings, recents and Start with Windows survive; the app still
      launches from both shortcuts
- [ ] Uninstall from Settings: program files, shortcuts and the
      `HKCU\...\Run\Intonaco` value are removed, while
      `%LocalAppData%\Intonaco\settings.json` remains for a future install

### Performance measurement

Record Task Manager / Performance Monitor values during the same 1080p
H.264 playback in each state. The helper script samples process RAM/CPU
for repeatable comparisons:

```powershell
.\tools\Measure-IntonacoPerformance.ps1 -Label "v1.3 playing main window" -DurationSeconds 20
# Minimize Intonaco to the tray, wait 10 seconds, sample again.
.\tools\Measure-IntonacoPerformance.ps1 -Label "v1.3 playing tray" -DurationSeconds 20
# Pause from the tray, wait 10 seconds, sample again.
.\tools\Measure-IntonacoPerformance.ps1 -Label "v1.3 paused" -DurationSeconds 20
```

- [ ] Record average/max private memory and working set for playing
      with the settings window visible, playing hidden in tray, paused,
      stopped idle, and fullscreen-paused; compare the same sequence
      with v1.2.0 where practical (v1.2.0 was reported around 100–150 MB
      playing and about 150 MB after Pause on the reporter's PC)
- [ ] Average CPU % and GPU Video Decode % are materially lower when
      the preview window is hidden than when it is visible, and fall to
      idle noise after Pause/fullscreen pause
- [ ] `debug.log` contains matching `Performance[paused-before-collection]`
      and `Performance[paused-after-collection]` lines; record private
      bytes from those lines as well as Task Manager

### Release hygiene

- [ ] Replace `docs/screenshot-placeholder.png` with a real screenshot of the
      main window (and ideally the live desktop)
- [ ] Confirm the published installer and zips run from a clean folder
      on a machine without the .NET SDK (installer/self-contained) /
      with only the runtime (framework-dependent)
- [ ] Confirm `SHA256SUMS.txt` matches the setup EXE, portable EXE and
      both zips in the release

## Known limitations

- WPF `MediaElement` renders at the video's native frame rate. The FPS
  preference is persisted, but a hard per-frame cap is not enforced on the
  `MediaElement` path; true throttling would need a Media Foundation
  pipeline with manual presentation. Tracked as future work.
- A suspended Pause reopens the file and seeks on Resume. Seek
  precision and reopen latency therefore depend on the container/codec
  and its keyframe/index layout; the static frame remains visible until
  Media Foundation presents new frames. If frame capture ever fails,
  Intonaco deliberately falls back to a native paused `MediaElement`
  rather than replacing the wallpaper with black.
- Format support is bounded by installed Windows codecs (HEVC needs the
  HEVC Video Extensions).
- The WorkerW technique relies on the undocumented `0x052C` Progman message.
  It has been stable since Windows 7, but Microsoft could change it in a
  future release.
