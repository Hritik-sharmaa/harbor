# Changelog

## 0.1.4 — 2026-10-26 (preview)

**Fixed**
- The Windows taskbar could slide up over the dock when you moved the mouse to the bottom edge. Windows shows its hidden taskbar again after the Windows key, Start, Search and some notifications, and Harbor only re-hid it every 4 seconds. Harbor now hides it the moment Windows shows it, within about 20 ms.

## 0.1.3 — 2026-10-25 (preview)

**Faster and lighter**
- Idle CPU is down from about 0.33% to 0.05% of the PC (1,604 to 234 ms per minute), and idle RAM from 45–51 MB to about 28 MB.
- Banners no longer check the notification list every 1.5 seconds. Windows records each new notification in the registry, so Harbor waits for that and does nothing until one arrives.
- The menu bar redraws its CPU/RAM/temperature readout only when a displayed number changes.
- Harbor frees memory once startup has settled and after the last banner disappears.

## 0.1.2 — 2026-10-25 (preview)

**New**
- macOS-style notification banners at the top right, under the menu bar, instead of Windows' pop-ups at the bottom right. They stack, pause while you hover, open the app on click, and stay quiet during Do Not Disturb, fullscreen apps and presentations. Windows keeps its own banners for alarms and system prompts, which need their buttons.
- While this is on, Harbor turns off Windows' "Show notification banners" for each app and turns it back on when Harbor quits, is uninstalled, or the option is switched off (**Harbor → General**). Notifications still collect in the Notification Center.

**Fixed**
- `--snapshot` could produce blank images or crash, because bitmap copies still pointed at window memory.

## 0.1.1 — 2026-10-25 (preview)

**Fixed**
- After waking from sleep or Modern Standby, the menu bar could shrink to nothing and the dock could stop responding. Windows gives monitors new handles when screens power back on; Harbor now looks them up again, re-measures the screens after wake, and never lays itself out on a screen that reports zero size.

## 0.1.0 — 2026-10-25 (preview)

First preview release. Harbor is young, so expect rough edges and please [report anything that breaks](https://github.com/Hritik-sharmaa/harbor/issues).

**Dock**
- Blurred dock with cursor-anchored magnification, at the bottom, left or right of the screen
- WhiteSur-style icons, rendered once at high resolution so resting and magnified icons match. Each app can use its own icon look, or a custom image
- Running dots, launch/attention bounce, unread badges, live window previews, minimized windows and folder stacks
- Smart Hide: the dock hides only when a window overlaps it, never on the desktop
- Per-app rules to hide the dock; it stays hidden during screenshots and over fullscreen apps on each monitor
- Web apps and Store apps hosted in a browser (Netflix, PWAs) get their own icon instead of the browser's, and apps with browser-based windows (Steam, Wallpaper Engine) show their real icon

**Menu bar**
- Full-width or "islands" style, on every display
- App menus, workspaces (virtual desktops), system tray, keyboard layout, mic and camera indicators
- Stats-style CPU, RAM and temperature readouts
- Bluetooth icon, plus headphone battery when headphones are connected
- Hot corners, off by default

**Panels**
- Control Center with Wi‑Fi, Bluetooth, media, focus, dark mode, a brightness slider for each display, and volume
- Custom Wi‑Fi, Bluetooth, Sound (per-app volume), Calendar, Battery and Media panels
- Notification Center with your real Windows notifications and widgets

**Harbor app**
- Every size, spacing, radius, colour, opacity, blur and border can be changed live
- Themes and presets, colours matched to your wallpaper, export and import as `.harbortheme`
- Hotkeys, settings backup, update check and a per-user installer and uninstaller
- Settings from the earlier "MacDock" builds are migrated automatically
