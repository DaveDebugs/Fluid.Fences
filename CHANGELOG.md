# Changelog

All notable changes to Fluid Fences are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [2.0.0] - 2026-07-23

The biggest release since the project started. The theming layer was rebuilt from
scratch, the app has a new look, and installing and updating are now handled by a
proper Windows installer. Installs in place over 1.3.x and keeps your fences, tabs,
and settings.

### Added
- New theming engine. Themes are generated from a shared set of design tokens rather
  than fixed color values, so a preset can be refined later and existing users pick up
  the improvement instead of being frozen on their first selection.
- Animated backgrounds on every theme.
- Auto-Match to wallpaper. Samples the current desktop wallpaper, builds a palette from
  it, and tints your fences to match. Fences follow when the wallpaper changes.
- New application icon and refreshed settings dashboard, with an animated logo on the
  Home page.
- Windows installer. Upgrades an existing 1.3.x install in place, offers to start with
  Windows, and asks before removing saved data on uninstall.

### Changed
- The app now ships as a self contained build, so no separate .NET runtime install is
  required.
- Auto-updater rewritten. It downloads the signed installer, verifies it with a SHA-256
  checksum before running anything, installs silently, and relaunches.
- The reported version is read directly from the build so it cannot drift out of sync.

### Fixed
- Wallpaper sampling on multi-monitor systems and when displays are changed at runtime.
- Hardened Folder Portals and the underlying file operations against edge cases.
- The app recovers cleanly from a corrupt or truncated config file instead of failing
  to start.
- The start-with-Windows entry is repointed correctly after an upgrade or a change of
  install location.
- General stability and security pass across the codebase, plus removal of dead code.

## [1.3.6]

- Hotfix: corrected auto-updating for installations under Program Files.

## [1.3.0]

- Added tabs, the in-app updater, and a range of UI enhancements.

## [1.2.5]

- Stability, security, and MVVM refactor. Async file I/O and modular native interop.
