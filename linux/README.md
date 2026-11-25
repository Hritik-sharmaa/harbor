# Harbor for Linux

Harbor's dock and live activities for **GNOME Shell 45 and newer**: Fedora Workstation 39+, Ubuntu 24.04 LTS and
later, and other distributions with GNOME on Wayland or X11.

It has two parts:

| Part | What it is | Where |
| --- | --- | --- |
| **GNOME Shell extension** | The dock (magnification, running dots, bounce, menus, Smart Hide) and the live activity pill | `gnome-extension/harbor@hritik-sharmaa.github.io` |
| **harbord** | Harbor's helper. It runs the same shared code as Harbor on Windows (`../src/Core`): reminders, timers and alarms, Pomodoro, Focus, break reminders, weather, lyrics, calendar, Claude usage, and your settings. D-Bus starts it the first time the extension asks | `harbord/` |

The extension works without harbord too: the dock then shows your GNOME favourites.

## Why an extension?

On Wayland, ordinary apps can't place a dock on the screen, reserve space for it, or see other apps' windows. GNOME
doesn't support the `layer-shell` protocol that KDE and wlroots desktops use for docks and bars. On GNOME, a dock has
to run inside GNOME Shell, which means an extension. Dash to Dock and Ubuntu Dock work the same way.

## Install

### Fedora

```bash
sudo dnf install ./harbor-0.4.2-1.x86_64.rpm
```

### Ubuntu

```bash
sudo apt install ./harbor_0.4.2-1_amd64.deb
```

The packages are on the [releases page](https://github.com/hritik-sharmaa/harbor/releases). Then log out and back in,
and turn Harbor on (as yourself, not root):

```bash
gnome-extensions enable harbor@hritik-sharmaa.github.io
```

On Ubuntu, turn Ubuntu Dock off so it doesn't sit on top of Harbor's dock:

```bash
gnome-extensions disable ubuntu-dock@ubuntu.com
```

### From source, just for you

You need the .NET 8 SDK (`sudo dnf install dotnet-sdk-8.0 clang zlib-devel` or
`sudo apt install dotnet-sdk-8.0 clang zlib1g-dev`), then:

```bash
cd linux
make user-install
```

That puts `harbord` in `~/.local/bin` and the extension in `~/.local/share/gnome-shell/extensions`. Log out and back in.
`make user-uninstall` removes it again; your settings stay in `~/.config/harbor`.

## Settings

harbord keeps the same `settings.ini` as Harbor on Windows, in `~/.config/harbor/`. On Linux, pinned apps are desktop
IDs (`firefox.desktop`, `org.gnome.Nautilus.desktop`). Reminders, notes, timers and the shelf sit next to it.

You can also talk to harbord directly:

```bash
gdbus call --session --dest io.github.hritik_sharmaa.Harbor --object-path /io/github/hritik_sharmaa/Harbor \
  --method io.github.hritik_sharmaa.Harbor1.Call settings.set '{"key": "magnifyPercent", "value": 170}'
```

```bash
gdbus call --session --dest io.github.hritik_sharmaa.Harbor --object-path /io/github/hritik_sharmaa/Harbor \
  --method io.github.hritik_sharmaa.Harbor1.Call timers.start '{"text": "25m", "label": "Tea"}'
```

## harbord's D-Bus interface

Bus name `io.github.hritik_sharmaa.Harbor`, object `/io/github/hritik_sharmaa/Harbor`, interface
`io.github.hritik_sharmaa.Harbor1`. Everything goes in and out as JSON, so front ends stay small.

| Member | What it does |
| --- | --- |
| `Version() → s` | harbord's version |
| `Get(s section) → s` | State of `dock`, `settings`, `reminders`, `notes`, `timers`, `pomodoro`, `focus`, `weather`, `calendar`, `claude`, `shelf`, `lyrics`, or `all` |
| `Call(s action, s args) → s` | An action, e.g. `reminders.add {"text", "due"}`, `timers.start {"text" \| "seconds", "label"}`, `pomodoro.toggle`, `focus.toggle`, `dock.pin {"id", "index"}`, `settings.set {"key", "value"}`, `weather.setCity {"name"}` (see `harbord/Api.cs` for all of them) |
| signal `Changed(s section)` | That section changed: Get it again |
| signal `Activity(s json)` | Show a live activity: title, detail, icon, tint, action button |
| signal `OpenTab(s tab)` | Open the island on a tab (for the island, still to come on Linux) |

Banners (a reminder is due, a Pomodoro phase ended) go to the desktop's own notification server.

## Build and test

```bash
make                # harbord as one native binary (NativeAOT) + the extension zip, in build/
make selftest       # harbord runs the shared code for real, without D-Bus
make smoke-test     # GNOME Shell runs headless with the extension; checks the dock, harbord and live activities
make packages       # .deb and .rpm (needs nfpm)
```

`AOT=false make` builds harbord as a normal .NET app instead (it then needs the .NET 8 runtime). For Fedora's COPR,
`packaging/harbor.spec` builds the RPM from a release tarball.

To try the extension in a nested GNOME Shell window without logging out:

```bash
dbus-run-session -- gnome-shell --devkit
```

On GNOME 48 and older, use `--nested --wayland` instead of `--devkit`.

## What's next

What's on Windows and not yet here: the menu bar and the island, Control Center and the panels (Wi‑Fi, Bluetooth,
sound, battery), the Notification Center, clipboard history, Screen Time, window previews, and the Claude Code
approval hook. Their logic is already shared in `src/Core` or maps onto GNOME's own services (NetworkManager, BlueZ,
UPower, PipeWire, MPRIS). They need GNOME Shell UI in the extension, plus the platform half in harbord.

A KDE Plasma front end could talk to the same harbord. Plasma supports `layer-shell`, so a regular app can draw a dock.
