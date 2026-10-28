# Changelog

## 0.2.3 — 2026-10-28 (preview)

**New in the island**
- **Clipboard:** your last 30 copies (text, links, images, files) and every new screenshot (Win+Shift+S, PrtScn, Win+PrtScn). Click one to copy it again; drag images and files straight into a browser, chat or folder. Filter by Text or Images. Text is kept in memory only, and copied images are deleted when Harbor quits. Copies that password managers mark as private are skipped. Turn it off in **Harbor → General → Island**.
- **Shelf:** drag files onto the centre of the menu bar. The island opens on the shelf; drop them there, then drag them out later wherever you need them. Click to open, right-click to show in Explorer. Only the paths are kept; the files stay where they are.
- **Claude Usage:** your plan limits (current session and this week) as Claude Code last saved them. That's the VS Code extension's copy if it's the newest; type `/usage` in Claude Code to update it. Also tokens per day for the last 7 days from Claude Code's history on this PC, split by editor (VS Code, Antigravity, Desktop & CLI) and by model. Everything is read locally and only while the tab is open; your login is never touched.

**Fixed**
- After an hour or so, an auto-hidden dock sometimes wouldn't come back when you moved the mouse to the bottom edge. Another window could end up over the thin strip the dock watches, so while hidden the dock now also checks the cursor position directly. When the cursor is at the edge, the dock comes up and puts itself back on top.

## 0.2.2 — 2026-10-27 (preview)

**Fixed**
- The notch and panels showed a square, blurred box with corners around their rounded shape. Windows' blur fills the whole window rectangle and ignores the rounded shape, so it's switched off for panels. They're now solid dark glass, with the sheen and light rim kept.

## 0.2.1 — 2026-10-27 (preview)

**New look**
- Every panel is dark frosted glass like the notch: the Control Center, Wi‑Fi, Bluetooth, Sound, Calendar, Battery, Notification Center, menus and folder stacks. Each has a glossy sheen, a soft light rim, 16 px rounded corners and glass tiles.
- The notch attaches to the menu bar (square top, round bottom), grows down when it opens and folds back up when it closes.
- A calmer Dashboard: three widgets (Up next, Focus, Weather) over one slim strip of apps and quick toggles.
- The Harbor app window has a dark background with soft glows, a frosted sidebar with a glossy selection, glass cards and glass segmented controls.
- A new app icon: a dark glass tile with the island at the top and a glass dock at the bottom.

**Claude Code**
- Approvals now work in VS Code, Antigravity and other editors that keep Claude Code's settings in their own folder (`.claude-vscode`, `.claude-antigravity`, `.claude-work`…). Harbor adds its hook to every Claude settings folder it finds, including new ones as they appear, and shows sessions from all of them.
- Questions (AskUserQuestion) appear in the notch with their options. Click one to answer; multi-select questions have a Send button; several questions are answered in turn. **Answer in editor** leaves it to the editor's own dialog.

**Better**
- Lyrics are found far more often. LRCLIB is retried when it's busy, matching no longer requires the album, tries the main artist alone and cleans up titles, and lyrics are remembered on your PC so repeat songs show them instantly. There's a Try again / Search again button.
- Pause, play and skip react immediately in the notch, and song changes appear as soon as Spotify has the new details.
- Wi‑Fi panel: a refresh button to scan for networks again.

**Fixed**
- Opening the notch no longer brings up an auto-hidden dock.
- A faint line across the middle of glass panels, where the sheen ended.

## 0.2.0 — 2026-10-27 (preview)

**New: the island.** Rest the mouse on the centre of the menu bar and a dark panel drops down, like a notch. It never takes keyboard focus from the app you're typing in, and it closes when the mouse moves away. Tabs:

- **Dashboard:** reminders, your most-used apps today, quick toggles (dark mode, Wi‑Fi, Bluetooth, keep awake, screenshot, lock), Pomodoro, day progress and weather.
- **Media:** cover, seek bar and controls with synced lyrics beside them.
- **Pomodoro:** ring timer with Work / Short break / Long break, a long break after every 4 sessions, sessions today, a focus target (one of your reminders), day progress, and a banner when time's up. While it runs, the time shows in the centre of the menu bar.
- **Weather:** from Open-Meteo for the city you set in **Harbor → General → Island**. Shows now, feels-like, humidity, wind, UV, rain chance, pressure, the next 12 hours, sunrise and sunset, with colour icons.
- **Screen Time:** today's time per app and per category, with a donut chart. Idle, locked and asleep time isn't counted, and it's kept on your PC.
- **Reminders:** quick reminders with optional due times (in 30 min, in 1 hour, this evening, tomorrow); a banner tells you when one is due.
- **AI Coding:** your Claude Code sessions with live status (Running / Waiting / Done), model, branch, messages and tokens. When Claude asks permission, the island pops up with **Allow / Always allow / Deny**. This works through a Claude Code hook that you switch on in **Harbor → General → Claude Code**: it's added to `%USERPROFILE%\.claude\settings.json` (backed up, other hooks untouched) and removed again when you switch it off or uninstall. If Harbor isn't running or you don't answer, Claude Code asks you as usual. Optional banner when Claude finishes.

**Also**
- Harbor's own banners (Pomodoro, reminders, Claude) use the same macOS-style banners as your notifications.
- The Now Playing panel and the island share one Now Playing view.
- Fixed: setting a weather city failed because Open-Meteo's compressed ("deflate") replies were misread. Harbor now asks for gzip only.
- Idle cost is unchanged: about 0.1% CPU and 27 MB RAM.

## 0.1.6 — 2026-10-26 (preview)

**New**
- Drag files out of a dock stack, such as Downloads, and drop them into a browser upload box, Gmail, a chat app or a folder. It works exactly like dragging from File Explorer, with the same file icon under the cursor. Dropping onto a folder on the same drive moves the file, as in Explorer; hold Ctrl to copy.

## 0.1.5 — 2026-10-26 (preview)

**New**
- Redesigned Now Playing panel. The background takes its colours from the album art, the cover is bigger, there's a progress bar with elapsed and remaining time that you can click or drag to seek, and the controls are larger.
- Time-synced lyrics from [LRCLIB](https://lrclib.net). The current line is highlighted and the view scrolls with the song; click a line to jump to it. Songs that only have unsynced lyrics show them in full. You can turn lyrics off with the **Lyrics** button in the panel or in **Harbor → General**.
- Privacy: this is Harbor's first network request. While the panel is open, it sends the song's title, artist, album and length to lrclib.net. See the README.

**Fixed**
- A rare crash when quitting Harbor, if a window moved at the moment it quit.

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
