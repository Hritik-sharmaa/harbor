#!/bin/sh
# The extension is installed for everyone but each person turns it on for themselves.
gtk-update-icon-cache -q -t /usr/share/icons/hicolor 2>/dev/null || true
cat <<'MSG'

Harbor is installed. To turn it on, log out and back in, then run (as yourself, not root):
    gnome-extensions enable harbor@hritik-sharmaa.github.io
On Ubuntu, turn Ubuntu Dock off so it doesn't cover Harbor's dock:
    gnome-extensions disable ubuntu-dock@ubuntu.com

MSG
exit 0
