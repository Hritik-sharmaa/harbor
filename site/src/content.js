// Every link and every piece of copy on the page lives here, so sections only deal with layout and motion.

export const GH = 'https://github.com/hritik-sharmaa/harbor'
export const DL = `${GH}/releases/latest`
export const CHANGELOG = `${GH}/blob/main/CHANGELOG.md`
export const shot = n => `screenshots/${n}.png`

export const navLinks = [
  ['Island', '#top'],
  ['Features', '#features'],
  ['Panels', '#panels'],
  ['Customize', '#customize'],
  ['Q&A', '#faq'],
]

// [screenshot id, tag, caption]
export const islandViews = [
  ['dashboard', 'Dashboard', 'Reminders, your most-used apps today, quick toggles, Pomodoro and the weather.'],
  ['media', 'Media', 'Cover, controls and synced lyrics. After the app closes, the last song is ready to play again.'],
  ['ai', 'AI Coding', 'Your Claude Code sessions with live status. When Claude asks permission, answer right here.'],
  ['weather', 'Weather', 'Feels-like, wind, UV, rain chance and the next 12 hours for your city.'],
  ['clipboard', 'Clipboard', 'Your last 30 copies and every new screenshot. Click to copy again, or drag out.'],
  ['pomodoro', 'Pomodoro', 'Work and break sessions on a ring timer, counting down in the menu bar.'],
  ['screentime', 'Screen Time', "Today's time per app and per category, kept on your PC."],
  ['reminders', 'Reminders', 'Quick reminders with optional due times. A banner tells you when one is due.'],
  ['usage', 'Claude Usage', 'Your plan limits and tokens per day for the last week, by editor and model.'],
]

export const heroSpecs = ['Windows 10 & 11', '20–40 MB of memory', '~0% CPU when idle', 'No admin rights', 'MIT licensed']

// [kind, label, detail] for the collapsed island
export const liveActivities = [
  ['charge', 'Charging', '82%'],
  ['download', 'Downloaded', 'report.pdf'],
  ['timer', 'Focus', '24:12'],
  ['meeting', 'Standup', 'in 5 min'],
]

export const panels = [
  ['f_cc', 338, 556, 'Control Center', 'Wi‑Fi, Bluetooth, Focus, brightness for every display and the sound output.'],
  ['f_media', 340, 474, 'Now Playing', 'Colours from the album art and lyrics that follow the song.'],
  ['f_nc', 372, 620, 'Notification Center', 'Your real Windows notifications, plus calendar and system widgets.'],
  ['f_wifi', 310, 277, 'Wi‑Fi', 'Networks, one click to join.'],
  ['f_cal', 300, 393, 'Calendar', 'The month at a glance from the clock.'],
  ['f_sys', 340, 590, 'System', 'CPU, memory and temperature, live.'],
  ['f_menu', 270, 512, 'App menus', "The active app's menus, at the left of the bar."],
]

// [tab, screenshot, title, text]
export const settingsPages = [
  ['Dock', 'w_settings1', 'A dock that sits where you want', 'Bottom, left or right. Size, magnification, spacing and Smart Hide, which hides it only when a window covers it.'],
  ['Themes', 'w_settings4', 'Looks that follow your wallpaper', 'Built-in presets, colours matched to your wallpaper, and .harbortheme files you can export and share.'],
  ['Menu Bar', 'w_settings2', 'Only the items you want', 'Full width or islands, on every monitor. Choose the readouts, the tray icons and whether it replaces the taskbar.'],
  ['General', 'w_settings0', 'Rules, shortcuts and backup', 'Hide the dock for specific apps, switch keyboard shortcuts on or off, and export every setting at once.'],
]

export const smallThings = [
  ['Out of the way of games', 'Steps aside for fullscreen video and games, on each monitor separately.'],
  ['Folder stacks', 'Drag files out of Downloads straight into an upload, a chat or a folder.'],
  ['Unread badges', '"Inbox (3)" in a window title becomes a badge on the icon.'],
  ['Web apps as apps', "Netflix and installed PWAs get their own icon instead of the browser's."],
  ['Headphone battery', "Next to the Bluetooth icon while they're connected."],
  ['Keyboard first', 'Alt Win Space opens the island. Alt Win V opens your clipboard.'],
]

export const connections = [
  ['lrclib.net', 'Lyrics, while on screen'],
  ['api.open-meteo.com', 'Weather, while open'],
  ['api.github.com', 'Update check, daily'],
]

export const claudeAsks = ['npm run build', 'git commit -m "ship it"', 'Edit src/Island.cs', 'dotnet test']

// topic -> [question, answer]
export const faq = {
  Install: [
    ['Is Harbor free?', 'Yes. Harbor is free and open source under the MIT license. There is no account, no trial and nothing to buy.'],
    ['Does it need admin rights?', 'No. It installs for your user only into %LOCALAPPDATA%\\Programs\\Harbor, adds a Start-menu shortcut and starts when you sign in.'],
    ['Windows warns about an unknown publisher.', "Harbor isn't code-signed yet, so SmartScreen may warn you. Choose More info, then Run anyway, or build it yourself from the source."],
    ['What does it need?', 'Windows 10 (1903 or later) or Windows 11, and .NET Framework 4.8, which already ships with Windows.'],
    ['Can I try it without installing?', 'Yes. Run Harbor.exe straight from the extracted folder.'],
  ],
  'Everyday use': [
    ['What happens to the Windows taskbar?', 'The menu bar can replace it entirely. Quitting or uninstalling Harbor always brings the taskbar back.'],
    ['Will it slow my PC down?', 'It is plain Win32 and GDI+, with no browser engine inside. It reacts to Windows shell events and only animates while something moves.'],
    ['How do Claude Code approvals work?', 'Switching them on adds a hook to your Claude Code settings, after backing them up. Whichever answers first wins: the island or your editor. Switching it off removes the hook.'],
    ['How do I uninstall it?', 'Settings → Apps → Installed apps → Harbor → Uninstall, or Harbor → About → Uninstall Harbor… Your settings stay in %APPDATA%\\Harbor until you delete the folder.'],
  ],
  Privacy: [
    ['Does Harbor collect anything?', 'No telemetry, no analytics, no account. Notifications, media and system stats are read to draw the screen and never stored or sent.'],
    ['When does it go online?', 'A daily update check with GitHub, lyrics from LRCLIB and weather from Open-Meteo while those views are open, and your calendar link if you paste one. Each can be switched off.'],
    ['What about my clipboard?', 'History lives in memory only, copied images are deleted when Harbor quits, and copies your password manager marks private are skipped.'],
  ],
}

export const installSteps = ['Download the zip', 'Run Install Harbor.cmd', 'Open Harbor from Start']
