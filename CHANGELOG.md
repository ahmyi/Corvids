# Changelog

All notable changes to Corvids are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project aims to follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet.

## [1.0.0] - 2026-09-15

First release. Corvids is a cross-platform desktop manager for Node.js apps, built with C# and Avalonia.

### Added

- **App entries** — register each app once with a name, project folder and command; edit or remove later.
  The Add dialog reads `package.json` and suggests its scripts, detecting npm, pnpm, yarn or bun from the
  lockfile. Per-app environment variables are supported.
- **Process control** — start, stop and restart per app, plus Start all and Stop all. Optional auto-start
  when Corvids opens and pm2-style auto-restart on crash (5 s delay, gives up after 10 crashes in 10 minutes).
- **Live logs** — a console per app with timestamps, tinted stderr, stripped ANSI colour codes and a
  5000-line buffer. Auto-scroll pauses when you scroll up and resumes at the bottom; wrap-lines, clear,
  copy-all, and Ctrl+C to copy the selection.
- **Reliable process kills** — on Windows each app runs inside a Job Object so Stop kills the whole tree,
  including grandchildren detached by Git Bash or npm. Restart waits for the app's port to be released.
- **Port-conflict recovery** — an `EADDRINUSE` in the log shows a banner offering to kill whatever holds
  the port and restart. Per-app "Kill node" clears stray node processes in the app's folder or on its port;
  "Kill all node processes" clears everything.
- **Console picker** — a "Run with" choice per app: Git Bash, cmd.exe, PowerShell, WSL or a custom command
  on Windows; the login shell, bash, zsh, pwsh or custom on macOS and Linux.
- **System tray** — closing the window offers to keep everything running in the tray or stop all and exit;
  the tray icon restores the window or exits.
- **Splash screen** on launch with the logo, motto and version; reopened from Settings ▸ About.
- **Settings** — run at sign-in (Windows Run key, macOS LaunchAgent, Linux XDG autostart), start minimized
  to the tray, the config file location, and a terminal command.
- **Live config** — `apps.json` is plain JSON and watched: edits from any editor are applied without a
  restart. Location overridable via `CORVIDS_CONFIG_DIR` or from Settings.
- **Cross-platform releases** — self-contained single-file builds for Windows x64/ARM64 and Linux x64/ARM64,
  and folder builds for macOS Intel/Apple Silicon, needing nothing installed on the target.
- **Tests** — an xUnit suite covering environment parsing, config and settings round-trips, shell command
  building, package.json script detection, and the port and node-process helpers.

### License

- Released under the GNU General Public License v3.0 or later.

[Unreleased]: https://github.com/ahmyi/Corvids/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/ahmyi/Corvids/releases/tag/v1.0.0
