# 🌊 Fluid Fences

A fast, open-source desktop organizer for Windows. Group your shortcuts, files, and
folders into clean translucent containers right on your desktop, and keep everything
where you can find it.

Built with C# and WPF for Windows 10 and 11.

![License](https://img.shields.io/badge/license-GPLv3-blue.svg)
![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-0078D6)

## Features

**Organizing your desktop**

- Tabbed fences. Put several tabs inside a single fence instead of spreading lists across the screen.
- Tear-off and merge. Drag a tab onto the desktop to spawn a new fence, or drop one fence onto another to combine them.
- Folder Portals. Mirror a real folder from your drive onto the desktop. A change in the portal changes the folder, and the other way around.
- Auto-Organize. Pick the file types you care about and Fluid Fences sweeps them off the desktop into the fence you choose.
- Snapshots. Save your layout so your fences come back to the right spot on every monitor.

**Look and feel**

- A set of built-in themes, each with an animated background.
- Auto-Match. Fluid Fences samples your wallpaper and tints your fences to match it. Change the wallpaper and the fences follow.
- Adjustable opacity, tint, and icon size.
- Ghost Mode. Fences stay nearly invisible until you hover over them.

**Getting around**

- Zen Mode. Press Ctrl + Alt + Z to hide every fence for a clean desktop, and again to bring them back.
- Roll-up. Double-click a title bar to collapse a fence down to its header.
- Search. Filter the icons in a fence from the search box.
- Sort by name, size, type, or date.

**Keeping it current**

- Start with Windows, if you want it to.
- Auto-updating. The app pulls new releases from GitHub, checks the download, and installs it for you.

## Installation

Download the latest installer from the [Releases page](https://github.com/DaveDebugs/Fluid.Fences/releases),
run it, and launch Fluid Fences.

- It installs over an older version in place and keeps your fences and settings.
- The build is self-contained, so you do not need to install .NET separately.
- Requires Windows 10 or 11, 64-bit.

## Getting started

Fluid Fences lives in the system tray, near the clock.

1. Create a fence. Right-click the tray icon and choose "Create New Fence".
2. Add files. Drag any file, folder, or shortcut into the fence.
3. Move and resize. Drag the header to move a fence, drag an edge or corner to resize it.

**Tabs**

- New tab: right-click a fence header and choose "New Tab Inside This Fence", or click the + button.
- Reorder: drag a tab left or right.
- Tear-off: drag a tab onto the empty desktop to turn it into its own fence.
- Merge: drag one fence's header onto another to combine them.

**Settings**

Double-click the tray icon to open the settings dashboard. From there you can pick a theme,
turn on Auto-Match, set your Auto-Organize file types, choose what happens to deleted files,
save a layout snapshot, and toggle start-with-Windows.

## Built with

- C# and .NET 8
- WPF (Windows Presentation Foundation)
- Win32 and COM interop for icon extraction and shell integration

## Contributing

Bug reports, ideas, and pull requests are welcome. Open an issue or a pull request on the repository.

## License

Fluid Fences is released under the GNU General Public License v3.0. See the [LICENSE](LICENSE)
file for the full text.

## Support

If the app is useful to you and you feel like chipping in:

- Buy Me a Coffee: [@Davedebugs](https://buymeacoffee.com/davedebugs)
- Venmo: [@Davedebugs](https://account.venmo.com/u/davedebugs)

© 2026 Davedebugs (David Daniel)
