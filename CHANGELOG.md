# Changelog

All notable changes to Intonaco are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and versions follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.2] — 2026-10-26

Fix for v1.0.1 reporting "Playing" while the actual desktop never
changed (reported on Windows 11 26H2, OS Build 26300.9711, with the
same 1920x1080 MP4).

### Fixed

- **Windows 11 24H2+ "raised desktop" support.** On these builds
  Progman is created with `WS_EX_NOREDIRECTIONBITMAP`, the icon view
  is a layered child of Progman, and Explorer's wallpaper is a WorkerW
  child below it — a plain child window parented there (v1.0.1's
  approach) is never composited by DWM: present and playing, but
  invisible. Following Microsoft's guidance to wallpaper apps,
  Intonaco now detects the raised desktop from the window tree and
  Progman's extended style and makes the player a `WS_EX_LAYERED`
  child of Progman with constant alpha `0xFF`
  (`SetLayeredWindowAttributes`, so WPF/Media Foundation presents stay
  hardware accelerated), z-ordered directly under `SHELLDLL_DefView`
  and above the system WorkerW. The classic top-level-WorkerW path is
  unchanged for Windows 10 / Windows 11 23H2 and earlier.
- The `0x052C` desktop-split request is now sent once as `(0xD, 1)`
  and only when the window tree shows no wallpaper layer yet — extra
  variants sent by v1.0.1 could undo the layer that was just created.
- A full-screen WorkerW under Progman is no longer confused with
  Explorer's tiny helper WorkerW windows (size-filtered).
- Attach now *verifies* visibility (parent, `IsWindowVisible`, and that
  the player actually covers its parent) before reporting success, and
  `%LocalAppData%\Intonaco\debug.log` records the OS build, the
  detected layout, Progman's child z-order and the player's final
  styles/rect for bug reports.

### Added

- Periodic placement self-check (every 5 s while a wallpaper is
  applied): if Explorer reshuffles its desktop windows — e.g. a
  wallpaper change recreates the system WorkerW above the player —
  size and z-order are re-asserted automatically.
- Unit tests for desktop-layout classification.

## [1.0.1] — 2026-10-26

Hotfix for "Failed to attach to desktop" on current Windows 11 builds
(reported against v1.0.0 with a 1920x1080 MP4; restarting Explorer did
not help because the lookup itself was too narrow).

### Fixed

- **Desktop attach (`DesktopIntegration`) rewritten for modern Explorer
  layouts.** v1.0.0 tried only two lookups with a single `0x052C` spawn
  variant. v1.0.1 sends all known spawn variants, enumerates top-level
  windows, and tries candidates in order — classic shell-host sibling,
  WorkerW child of Progman (newer Windows 11 / Spotlight layouts),
  WorkerW after Progman, any icon-less WorkerW, and finally Progman
  itself with the player Z-ordered to the bottom — retrying briefly
  while Explorer creates the WorkerW asynchronously, and verifying
  `SetParent` actually took effect before reporting success.
- Player window conversion now clears `WS_POPUP` (mutually exclusive
  with `WS_CHILD`) and uses `Get/SetWindowLongPtr` on 64-bit Windows.
- A failed attach no longer leaves the black player window floating on
  screen; it is closed, and the real failure reason is shown.
- Footer version now comes from the assembly version (was hardcoded).

### Added

- Diagnostic logging to `%LocalAppData%\Intonaco\debug.log`: every
  attach attempt records the Progman/WorkerW windows found, the
  candidates tried and Win32 errors — attach this file to bug reports.
- Direct single-file download in releases:
  `Intonaco-v<version>-win-x64.exe` (self-contained, no unzip needed),
  included in `SHA256SUMS.txt`.
- Unit tests for WorkerW candidate ordering.

## [1.0.0] — 2026-10-26

First official release.

### Added

- Complete WPF application on .NET 8 with a Windows 11-style dark theme UI
  (Segoe UI Variable, card layout, Segoe MDL2 icon glyphs).
- Real desktop integration via the WorkerW technique
  (`DesktopIntegration`): sends `0x052C` to Progman, locates the WorkerW
  sibling of the SHELLDLL_DefView host, and parents the borderless
  `PlayerWindow` behind the desktop icons.
- Video wallpaper playback via WPF `MediaElement` / Windows Media Foundation
  with DXVA2 hardware decoding (`WallpaperPlayer`) — no libvlc, eliminating
  ~120 MB of native binaries compared to the original AutoWall approach.
- Play / pause / stop / mute / volume controls, muted by default, with
  seamless looping and a live (muted) in-window preview.
- `FullscreenDetector`: 2-second polling of the foreground window against its
  monitor rect; automatically pauses playback for fullscreen games/apps.
- `SystemEventMonitor`: pauses on sleep/lock, resumes after wake/unlock
  (with a short display-stabilization delay), and re-fits the wallpaper on
  display changes.
- Explorer restart recovery via the `TaskbarCreated` broadcast message.
- System tray icon (Hardcodet.NotifyIcon.Wpf) with Show / Play-Pause / Mute /
  Exit menu, minimize-to-tray, and double-click to restore.
- JSON settings persistence in `%LocalAppData%\Intonaco\settings.json`
  (last wallpaper, volume, mute, FPS limit, pause-on-fullscreen, theme,
  start-with-Windows), auto-play of the last wallpaper on launch, and
  `--minimized` start support.
- Start-with-Windows registration under `HKCU\...\Run` (`StartupManager`).
- Configurable frame-rate limit (15/24/30/60 fps, `FrameRateLimiter`).
- Single-instance enforcement via a named mutex; PerMonitorV2 DPI-aware
  application manifest.
- Light theme with a System/Light/Dark selector that follows the Windows
  app theme (including live switching on system theme changes).
- Multi-resolution application icon (`16–256 px` `.ico`) generated from the
  SVG source in `src/Intonaco/Assets/` by `tools/generate-icon.py`.
- xUnit test suite (settings defaults/clamping, player state machine and
  clamping, fullscreen detector lifecycle, frame-rate limiter).
- GitHub Actions CI (build + test + publish artifacts on Windows) and a
  tag-driven release workflow producing framework-dependent and
  self-contained single-file zips with `SHA256SUMS.txt`.
- Unhandled-exception logging to `%LocalAppData%\Intonaco\crash.log`.
- Manual Windows verification checklist in `docs/VERIFICATION.md`.

### Fixed (during the initial hardening pass)

- The second-instance path no longer calls `ReleaseMutex` on a mutex owned
  by the first process (threw `ApplicationException` on exit).
- FPS setting now persists: settings events are suppressed during XAML
  initialization so the default 30 fps selection no longer overwrites the
  saved value on every launch, and saved FPS/volume/mute are pushed into the
  player at startup.
- The Stopped state handler no longer blanks the file name (an async
  `BeginInvoke` race made it clear the name of a just-applied wallpaper).
- Explorer-restart/wake recovery now fully re-applies the wallpaper with a
  fresh player window when the old WorkerW (and its child window) was
  destroyed, instead of silently failing to reattach.
- `DesktopIntegration.Attach` no longer propagates handle-creation
  exceptions from destroyed windows; it reports failure so callers can
  recover.
- Test project compiles (missing `using System;`) and WPF-touching tests run
  on an STA thread with a Dispatcher via `Xunit.StaFact` (`[WpfFact]`),
  instead of failing on xUnit's default MTA threads.
- Solution file uses `Any CPU` configurations that match the SDK-style
  projects (the original `x64`-only mappings did not build with
  `dotnet build`).
- `RuntimeIdentifier`/`SelfContained` moved out of the app project into the
  publish commands (`build.ps1`, CI) so `dotnet build`/`dotnet test` work
  RID-agnostically.
- README: fixed the placeholder clone URL, an unclosed code fence, and the
  flattened Usage/Performance tables.

[1.0.2]: https://github.com/77worldd/Intonaco/releases/tag/v1.0.2
[1.0.1]: https://github.com/77worldd/Intonaco/releases/tag/v1.0.1
[1.0.0]: https://github.com/77worldd/Intonaco/releases/tag/v1.0.0
