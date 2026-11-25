#!/bin/bash
# Runs GNOME Shell headless with Harbor's extension and checks that it works:
#   the extension loads, D-Bus starts harbord, the dock shows the favourites and magnifies under the pointer,
#   a timer from harbord shows a live activity, disable/enable leaves nothing behind, and no JS errors are logged.
#
#   tests/shell-smoke.sh /path/to/harbord      test this harbord with the extension from this checkout
#   tests/shell-smoke.sh --installed           test what a package installed (/usr/share/..., /usr/bin/harbord)
#   SHOTS=dir tests/shell-smoke.sh ...         also save screenshots there
#
# Needs gnome-shell (45+), gdbus and gsettings. Exit code = number of failed checks.
set -u
HERE=$(cd "$(dirname "$0")" && pwd)
UUID=harbor@hritik-sharmaa.github.io
BUS=io.github.hritik_sharmaa.Harbor

if [ -z "${HARBOR_SMOKE_INNER:-}" ]; then
    MODE=${1:?usage: shell-smoke.sh /path/to/harbord | --installed}
    T=$(mktemp -d /tmp/harbor-smoke.XXXXXX)
    export HOME=$T XDG_CONFIG_HOME=$T/.config XDG_DATA_HOME=$T/.local/share XDG_CACHE_HOME=$T/.cache
    export XDG_RUNTIME_DIR=$T/run; mkdir -p -m 700 "$XDG_RUNTIME_DIR"
    EXT=$XDG_DATA_HOME/gnome-shell/extensions; mkdir -p "$EXT"
    cp -r "$HERE/unsafe-mode@harbor.test" "$EXT/"
    if [ "$MODE" != "--installed" ]; then
        cp -r "$HERE/../gnome-extension/$UUID" "$EXT/"
        mkdir -p "$XDG_DATA_HOME/dbus-1/services"
        printf '[D-BUS Service]\nName=%s\nExec=%s\n' "$BUS" "$(realpath "$MODE")" > "$XDG_DATA_HOME/dbus-1/services/$BUS.service"
    fi
    # four small GTK apps for the dock
    mkdir -p "$XDG_DATA_HOME/applications"
    cat > "$T/winapp.js" <<'JS'
imports.gi.versions.Gtk = '4.0';
const {Gtk} = imports.gi;
const app = new Gtk.Application({application_id: 'harbortest.' + (ARGV[0] || 'app')});
app.connect('activate', () => new Gtk.ApplicationWindow({application: app, title: 'Test ' + ARGV[0], default_width: 800, default_height: 600}).present());
app.run([]);
JS
    for a in files:Files:system-file-manager web:Web:web-browser terminal:Terminal:utilities-terminal editor:Editor:accessories-text-editor; do
        IFS=: read -r id name icon <<<"$a"
        printf '[Desktop Entry]\nType=Application\nName=%s\nExec=gjs %s %s\nIcon=%s\nStartupWMClass=harbortest.%s\n' \
            "$name" "$T/winapp.js" "$id" "$icon" "$id" > "$XDG_DATA_HOME/applications/harbortest-$id.desktop"
    done
    # GNOME Shell needs a system bus (logind, accounts); containers often have none
    if [ ! -S /run/dbus/system_bus_socket ]; then
        SUDO=; [ "$(id -u)" != 0 ] && SUDO="sudo -n"
        $SUDO mkdir -p /run/dbus && $SUDO dbus-daemon --system --fork || echo "warning: no system bus"
    fi
    HARBOR_SMOKE_INNER=1 T=$T dbus-run-session -- "$0" "$@"
    rc=$?
    rm -rf "$T"
    exit $rc
fi

# ---------------------------------------------------------------- inside a fresh session bus
fails=0
check() { if [ "$1" = ok ]; then echo "  ok    $2"; else echo "  FAIL  $2"; fails=$((fails + 1)); fi; }
ev() { gdbus call --session --dest org.gnome.Shell --object-path /org/gnome/Shell --method org.gnome.Shell.Eval "$1" 2>/dev/null | sed -E "s/^\(true, '(.*)'\)$/\1/; s/^\"(.*)\"$/\1/"; }
shot() { [ -n "${SHOTS:-}" ] && mkdir -p "$SHOTS" && gdbus call --session --dest org.gnome.Shell.Screenshot --object-path /org/gnome/Shell/Screenshot \
    --method org.gnome.Shell.Screenshot.Screenshot false false "$SHOTS/$1.png" >/dev/null 2>&1; }
harbord() { gdbus call --session --dest $BUS --object-path /io/github/hritik_sharmaa/Harbor --method io.github.hritik_sharmaa.Harbor1."$@" 2>&1; }
DOCK="Main.layoutManager.uiGroup.get_children().filter(a => a.name === 'harborDock')"

gsettings set org.gnome.shell disable-user-extensions false
gsettings set org.gnome.shell enabled-extensions "['unsafe-mode@harbor.test', '$UUID']"
gsettings set org.gnome.shell favorite-apps "['harbortest-files.desktop', 'harbortest-web.desktop', 'harbortest-terminal.desktop', 'harbortest-editor.desktop']"
gsettings set org.gnome.shell welcome-dialog-last-shown-version '999'

LOG=$T/shell.log
gnome-shell --headless --virtual-monitor 1600x900 --wayland --no-x11 --sm-disable > "$LOG" 2>&1 &
SHELL_PID=$!
for _ in $(seq 1 90); do grep -q "HARBOR-TEST: unsafe mode on" "$LOG" 2>/dev/null && break; sleep 1; done
sleep 3
echo "GNOME Shell $(gnome-shell --version | awk '{print $3}') headless, testing Harbor"

state=$(gnome-extensions info $UUID 2>/dev/null | sed -n 's/.*State: //p')
[ "$state" = ACTIVE ] && check ok "extension is active" || check fail "extension is active (state: ${state:-unknown})"
v=$(harbord Version)
[[ "$v" == *"'"*"'"* ]] && check ok "D-Bus started harbord $v" || check fail "D-Bus started harbord: $v"

ev "Main.overview.hide()" >/dev/null; sleep 1.5
n=$(ev "$DOCK[0]?.get_n_children()")
[ "$n" = 4 ] && check ok "the dock shows the 4 favourites" || check fail "the dock shows the 4 favourites (got '$n')"

ev "globalThis.__vp = imports.gi.Clutter.get_default_backend().get_default_seat().create_virtual_device(imports.gi.Clutter.InputDeviceType.POINTER_DEVICE); 1" >/dev/null
geom=$(ev "const d = $DOCK[0]; [Math.round(d.x + d.width * 0.375), Math.round(d.y + d.height / 2)].join(' ')")
read -r px py <<<"$geom"; [ -n "${DEBUG:-}" ] && echo "  debug: geom=[$geom] dock=$(ev "JSON.stringify([$DOCK[0].x,$DOCK[0].y,$DOCK[0].width,$DOCK[0].height])")"
ev "__vp.notify_absolute_motion(global.get_current_time() * 1000, $px, 400)" >/dev/null; sleep 0.3
ev "__vp.notify_absolute_motion(global.get_current_time() * 1000, $px, $py)" >/dev/null; sleep 0.6
[ -n "${DEBUG:-}" ] && echo "  debug: pointer=$(ev "global.get_pointer().slice(0,2).join(\",\")") hover=$(ev "$DOCK[0].hover")"
big=$(ev "Math.max(...$DOCK[0].get_children().map(c => c.get_children()[0]?.icon_size ?? 0))")
small=$(ev "Math.min(...$DOCK[0].get_children().map(c => c.get_children()[0]?.icon_size ?? 999))")
[ "${big:-0}" -gt "${small:-0}" ] 2>/dev/null && check ok "icons magnify under the pointer ($small → $big px)" || check fail "icons magnify under the pointer ($small / $big)"
shot dock-hover
ev "__vp.notify_absolute_motion(global.get_current_time() * 1000, 800, 300)" >/dev/null; sleep 0.5

harbord Call timers.start '{"seconds": 1, "label": "Smoke test"}' >/dev/null
sleep 2.2
pill=$(ev "Main.layoutManager.uiGroup.get_children().some(a => a.has_style_class_name?.('harbor-pill'))")
[ "$pill" = true ] && check ok "a harbord timer shows a live activity" || check fail "a harbord timer shows a live activity ($pill)"
shot live-activity

for _ in 1 2 3; do gnome-extensions disable $UUID; sleep 0.6; gnome-extensions enable $UUID; sleep 1; done
[ "$(ev "$DOCK.length")" = 1 ] && check ok "3 disable/enable cycles leave one dock" || check fail "3 disable/enable cycles leave one dock ($(ev "$DOCK.length"))"
gnome-extensions disable $UUID; sleep 0.6
left=$(ev "Main.layoutManager.uiGroup.get_children().filter(a => a.name?.startsWith('harborDock') || a.has_style_class_name?.('harbor-dock-label') || a.has_style_class_name?.('harbor-pill')).length")
[ "$left" = 0 ] && check ok "disable removes everything" || check fail "disable removes everything ($left left)"

errs=$(grep -E "JS ERROR|Gjs-CRITICAL" "$LOG" | grep -ciE "harbor|dock.js|client.js|activity.js|extension.js")
[ "$errs" = 0 ] && check ok "no JS errors from Harbor in the shell log" || { check fail "no JS errors from Harbor ($errs)"; grep -E -A6 "JS ERROR|Gjs-CRITICAL" "$LOG" | head -40; }

kill $SHELL_PID 2>/dev/null; wait $SHELL_PID 2>/dev/null
echo "$fails check(s) failed."
exit $fails
