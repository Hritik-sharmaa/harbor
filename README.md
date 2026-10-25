<p align="center">
  <img src="assets/Harbor.png" width="96" alt="Harbor icon">
</p>

<h1 align="center">Harbor</h1>

<p align="center">
  A macOS-style dock, menu bar and Control Center for Windows 10 and 11.<br>
  Native, about 20–40 MB of RAM, close to 0% CPU when idle, and starts with Windows.
</p>

<p align="center">
  <a href="https://github.com/hritik-sharmaa/harbor/releases/latest"><b>Download</b></a> ·
  <a href="#features">Features</a> ·
  <a href="#build-from-source">Build</a> ·
  <a href="CHANGELOG.md">Changelog</a>
</p>

![Harbor on a Windows desktop](docs/screenshots/hero.png)

## Why Harbor

Most dock replacements are web apps that run a whole browser in the background. Harbor is plain Win32 and GDI+ code with no Electron, no runtime to install and no polling loops. It reacts to Windows shell events and only animates while something is moving, so it stays out of the way even with many apps open.

## Features

### Dock

<img src="docs/screenshots/dock.png" alt="Dock" width="100%">

- **Looks**
  - Blurred, translucent dock with smooth magnification that follows the cursor
  - Can sit at the **bottom, left or right** of the screen
  - **WhiteSur-style icons** generated from each app's own icon
  - Choose an icon look per app (full colour, brand, white, dark or original), or use any image as a custom icon
- **Apps and windows**
  - Running dots, launch and attention bounce, and **unread badges** (for example "Inbox (3)")
  - **Live window previews** on hover
  - **Minimized windows** and **folder stacks** in the dock
  - Web apps and Store apps that run inside a browser (Netflix, installed PWAs) get their own icon instead of the browser's
- **Pinning and editing**
  - Drag to reorder, or drag out to remove
  - Drop a file, folder or `.exe` onto the dock to pin it
  - Right-click for windows, New Window, Keep in Dock, Show in Explorer and Quit
- **Hiding**
  - **Smart Hide** hides the dock only when a window covers it; on the desktop it always shows
  - Hides over fullscreen video and games on each monitor separately, and stays hidden while you take a screenshot

### Menu bar

- **Layout**
  - Full width or "islands" style, on every monitor
  - Can replace the Windows taskbar entirely
- **Left side**
  - The active app's name and its **app menus**
  - **Workspaces** (virtual desktops)
- **Right side**
  - **System tray**, Wi‑Fi, volume and battery
  - **Bluetooth** icon, with **headphone battery** when headphones are connected
  - **CPU, RAM and temperature** readouts, keyboard layout, and **mic and camera in-use dots**
- **Hot corners**, off by default

### Control Center and panels

<p>
  <img src="docs/screenshots/f_cc.png" alt="Control Center" width="49%">
  <img src="docs/screenshots/f_nc.png" alt="Notification Center" width="40%">
</p>

- **Control Center**
  - Wi‑Fi, Bluetooth and Airplane mode
  - Now Playing, Focus and Dark Mode
  - A **brightness slider for every display**: DDC/CI for external monitors, the laptop panel, or software dimming as a fallback
  - Volume
- **Panels** that open from the menu bar instead of the Windows ones:
  - **Wi‑Fi**: networks list
  - **Bluetooth**: devices with battery levels
  - **Sound**: per-app volume
  - **Calendar**, **Battery** and **Media**
- **Notification Center** shows your real Windows notifications, plus calendar, now-playing and system widgets

### Customize everything

<p>
  <img src="docs/screenshots/w_settings1.png" alt="Dock settings" width="49%">
  <img src="docs/screenshots/w_settings4.png" alt="Themes" width="49%">
</p>

Open the **Harbor** app from the Start menu. If Harbor is already running, launching it again opens this window.

- **Change live**
  - Sizes, spacing, padding and corner radius
  - Colours, opacity, blur and borders
  - Which items appear in the menu bar
- **Themes**
  - Built-in presets
  - **Match wallpaper colours**
  - Export and import `.harbortheme` files
- **Rules**
  - Hide the dock for specific apps
  - Hide individual apps from the dock
- **Backup**: export and import all settings

### Keyboard shortcuts

| Shortcut | Action |
| --- | --- |
| <kbd>Alt</kbd> + <kbd>Win</kbd> + <kbd>N</kbd> | Notification Center |
| <kbd>Alt</kbd> + <kbd>Win</kbd> + <kbd>C</kbd> | Control Center |
| <kbd>Alt</kbd> + <kbd>Win</kbd> + <kbd>D</kbd> | Toggle dock auto-hide |

You can turn these off in **Harbor → General**.

## Install

1. Download `Harbor-vX.Y.Z.zip` from [Releases](https://github.com/hritik-sharmaa/harbor/releases/latest) and extract it.
2. Double-click **`Install Harbor.cmd`**.

Harbor installs for your user only, without admin rights, into `%LOCALAPPDATA%\Programs\Harbor`. It adds a Start-menu shortcut and an entry in **Apps & features**, then starts automatically at sign-in.

To try it without installing, run `Harbor.exe` straight from the extracted folder.

> **Windows SmartScreen** may warn that the app is from an unknown publisher, because the build isn't code-signed. Click **More info → Run anyway**. You can also [build it yourself](#build-from-source) from the source in this repository.

**Requirements**
- Windows 10 (1903 or later) or Windows 11
- .NET Framework 4.8, which ships with Windows

## Uninstall

Use any of these:
- **Settings → Apps → Installed apps → Harbor → Uninstall**
- **Harbor → About → Uninstall Harbor…**

Quitting or uninstalling always brings the Windows taskbar back. Your settings stay in `%APPDATA%\Harbor` until you delete that folder.

## Build from source

You don't need Visual Studio or any SDK. Harbor builds with the C# compiler that ships with Windows:

```bat
build.cmd
```

That produces `bin\Harbor.exe`. To install your build, run:

```bat
bin\Harbor.exe --install
```

| Command-line flag | What it does |
| --- | --- |
| `--install` | Installs or updates this build for the current user and starts it |
| `--quit` | Closes the running Harbor |
| `--cc` | Opens the Control Center (you can bind this to a key in another tool) |
| `--uninstall` | Removes Harbor |
| `--snapshot <dir> [--demo]` | Renders the UI to PNG files; `--demo` uses sample data. The README screenshots are made this way |

### How it fits together

| File | Responsibility |
| --- | --- |
| `src/App.cs` | Startup, displays, shell events, fullscreen and auto-hide rules |
| `src/Dock.cs`, `src/Previews.cs` | Dock layout, magnification, badges, stacks and DWM window previews |
| `src/TopBar.cs`, `src/TrayService.cs` | Menu bar, app menus, status icons and system tray |
| `src/ControlCenter.cs`, `src/Flyouts.cs`, `src/NotificationCenter.cs` | Panels |
| `src/Flyout.cs` | Small immediate-mode UI toolkit used by every panel and by the Harbor app |
| `src/SettingsWindow.cs`, `src/Look.cs`, `src/Settings.cs` | The Harbor app and `settings.ini` |
| `src/ShellIcons.cs` | Icon loading and the WhiteSur-style icon renderer |
| `src/SystemStatus.cs`, `src/SysInfo.cs`, `src/BtBattery.cs`, `src/WinRT.cs` | Wi‑Fi, audio, brightness, stats, Bluetooth, media and notifications |

## Privacy

Harbor makes **no network requests**. It has no telemetry, analytics or auto-download. **Check for Updates** just opens the Releases page in your browser.

Notifications, media info and system stats are read locally to draw the UI and are never stored or sent anywhere.

## Credits

- Icon style inspired by [WhiteSur](https://github.com/vinceliuice/WhiteSur-icon-theme) by vinceliuice
- Inspired by macOS and [Seelen UI](https://github.com/eythaann/Seelen-UI)

Harbor isn't affiliated with Apple or Microsoft. macOS is a trademark of Apple Inc.

## License

[MIT](LICENSE) © 2026 Hritik Sharma
