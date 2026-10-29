# Changelog

## 0.3.0 — 2026-10-29 (preview)

**New in the notch**
- **Choose your tabs:** in **Harbor → General → Notch tabs**, switch off the tabs you don't use and reorder the rest. AI Coding and Claude Usage start hidden on PCs without Claude Code.
- **Keyboard:**
  - **Alt+Win+Space** opens the notch on the screen you're using. ←/→, Tab or 1–9 switch tabs; Esc closes it.
  - **Alt+Win+V** opens Clipboard: ↑/↓ pick a copy and Enter pastes it into the app you were in.
- **Live activities:** a small pill drops out of the menu bar for a few seconds, on the screen you're working on. Click it to act.
  - Charging started or stopped, with the battery level.
  - Headphones connected, with their battery.
  - A download finished (Open).
  - A screenshot was taken (Show).
  - A timer or alarm is done (+1 min / Snooze).
  - A meeting starts in 5 minutes (Join).

  Quiet over fullscreen apps; only timers, alarms and meetings come through during Focus.
- **Timers:** quick 1/5/10/15/25-minute timers or any length ("12", "1:30", "45s"), several at once, with pause. The soonest one counts down in the menu bar.
- **Alarms and world clocks:** alarms ("7:30 Wake up", "6:15 pm gym") with a switch each, and clocks for any city with the time difference.
- **Calendar:** paste your calendar's private iCal link (Google or Outlook, no sign-in) in **Harbor → General → Calendar**.
  - The Calendar tab shows the next meeting with **Join meeting**, plus today's and tomorrow's agenda.
  - The Dashboard's Up next starts with your next meeting.
  - Teams, Meet and Zoom links are found automatically.
  - Repeating meetings, moved or cancelled ones, and time zones (with daylight saving) are handled.
- **Notes:** jot down short notes. Click one to copy it, pin the ones you reuse.
- **Downloads:** what's downloading now and your latest 8 files. Open them, drag them out, or right-click to show them in Explorer.
- **Last played:** when the music app is closed, the Media tab shows the last song. Play opens Spotify (or that app) and carries on with it; for browser media it offers a YouTube search.
- **Break reminders:** rest your eyes every 20 minutes and stand up every hour (both on); drink water every 90 minutes (off). Only time at the PC counts, and they wait during Focus and fullscreen.
- **Device batteries:** the Battery panel lists connected Bluetooth devices that report a battery level.

**Fixed**
- **Claude Code questions answered in the editor stay in the notch.** Claude Code doesn't tell Harbor when you answer in VS Code's own prompt. While a question is open, Harbor now watches that session's history for the answer to that exact tool call, so the notch clears within about a second of you answering in the editor. A new message or Claude finishing clears it too.
- **The notch pops up while you're already in the editor that's asking.** Harbor now knows which app the question came from (VS Code, Antigravity, a terminal). If that app is in front, the notch stays closed and you answer there. If you switch to another app while Claude is still waiting, the notch pops up then.

## 0.2.4 — 2026-10-28 (preview)

**New**
- **Focus:** one switch for heads-down time, in Control Center, the Dashboard, the Harbor menu and **Harbor → General → Focus**. While it's on:
  - Banners stay quiet; notifications still collect in the Notification Center, and when Focus ends Harbor tells you how many came in.
  - A Pomodoro work session starts, and Focus ends with it.
  - The dock tucks away until you move the mouse to the screen edge.
  - A moon in the menu bar shows the minutes left; click it to end Focus. Both the Pomodoro and the dock behaviour can be switched off.
- **In-app updates:** Harbor checks GitHub for a new release once a day and tells you when one is out. **Harbor → About** shows what's new; **Install** downloads it, checks it against GitHub's SHA-256 checksum, installs it and restarts Harbor. It's also in the Harbor menu (top left). Nothing about you is sent; switch the daily check off in **About**.
- **Sound output switching:** the Sound panel lists your outputs (speakers, headphones, HDMI…) to switch between, and the notch's Media tab shows the current one; click it to move to the next. Shown only when you have more than one.
- **Brightness from the menu bar:** scroll over the Control Center icon to change that screen's brightness, like scrolling over the volume icon.

**Fixed**
- **Claude Usage** could show another editor's account. VS Code, Antigravity and the CLI can each be signed in to a different Claude account, and Harbor used whichever had saved its numbers most recently. It now uses VS Code's numbers when VS Code has any.
- **Claude Usage display:** a session that hasn't started says "Starts with your next message". Weekly resets read "Resets in 6d · Wed 00:29", like Claude's own dialog. The footer shows when the numbers were last updated.

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
