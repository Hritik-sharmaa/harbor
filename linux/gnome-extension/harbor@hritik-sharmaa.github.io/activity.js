// Live activities: the small black pill that drops from the top of the screen when a timer rings, a reminder
// is due or a meeting starts in five minutes. harbord sends them as Activity signals; a click on the action
// ("+1 min", "Join") goes back as Call("activity.click").

import Clutter from 'gi://Clutter';
import GLib from 'gi://GLib';
import St from 'gi://St';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';

export class LiveActivities {
    constructor(client) {
        this._client = client;
        this._queue = [];
        this._pill = null;
        this._timeout = 0;
        client.on('activity', a => this.show(a));
    }

    show(a) {
        if (!a || !a.title) return;
        if (a.important) this._queue.unshift(a); else this._queue.push(a);
        if (!this._pill) this._next();
    }

    _next() {
        const a = this._queue.shift();
        if (!a) return;
        const m = Main.layoutManager.primaryMonitor;
        if (!m) return;

        const pill = new St.BoxLayout({style_class: 'harbor-pill', reactive: true, track_hover: true});
        const icon = new St.Icon({icon_name: a.icon || 'dialog-information-symbolic', style_class: 'harbor-pill-icon', y_align: Clutter.ActorAlign.CENTER});
        if (a.tint) icon.set_style(`color: ${a.tint};`);
        const text = new St.BoxLayout({vertical: true, x_expand: true, y_align: Clutter.ActorAlign.CENTER});
        text.add_child(new St.Label({text: a.title, style_class: 'harbor-pill-title'}));
        if (a.detail) text.add_child(new St.Label({text: a.detail, style_class: 'harbor-pill-detail'}));
        pill.add_child(icon);
        pill.add_child(text);
        if (a.action && a.id) {
            const btn = new St.Button({label: a.action, style_class: 'harbor-pill-action', y_align: Clutter.ActorAlign.CENTER});
            btn.connect('clicked', () => {
                this._client.call('activity.click', {id: a.id});
                this._dismiss();
            });
            pill.add_child(btn);
        }
        pill.connect('button-release-event', () => {
            this._dismiss();
            return Clutter.EVENT_STOP;
        });
        // keep it up while the pointer rests on it
        pill.connect('notify::hover', () => { if (!pill.hover) this._arm(1500); else this._disarm(); });

        Main.layoutManager.addTopChrome(pill);
        this._pill = pill;
        const [, w] = pill.get_preferred_width(-1);
        const top = m.y + Main.panel.height + 6;
        pill.set_position(Math.round(m.x + (m.width - w) / 2), top - 20);
        pill.opacity = 0;
        pill.set_pivot_point(0.5, 0);
        pill.scale_x = 0.6;
        pill.ease({y: top, opacity: 255, scale_x: 1, duration: 320, mode: Clutter.AnimationMode.EASE_OUT_BACK});
        this._arm(a.ms > 0 ? a.ms : 4000);
    }

    _arm(ms) {
        this._disarm();
        this._timeout = GLib.timeout_add(GLib.PRIORITY_DEFAULT, ms, () => {
            this._timeout = 0;
            this._dismiss();
            return GLib.SOURCE_REMOVE;
        });
    }

    _disarm() {
        if (this._timeout) GLib.source_remove(this._timeout);
        this._timeout = 0;
    }

    _dismiss() {
        this._disarm();
        const pill = this._pill;
        if (!pill) return;
        pill.ease({
            opacity: 0, y: pill.y - 16, duration: 200, mode: Clutter.AnimationMode.EASE_IN_QUAD,
            onComplete: () => {
                Main.layoutManager.removeChrome(pill);
                pill.destroy();
                if (this._pill === pill) this._pill = null;
                this._next();
            },
        });
    }

    destroy() {
        this._disarm();
        this._queue = [];
        if (this._pill) {
            this._pill.remove_all_transitions();
            Main.layoutManager.removeChrome(this._pill);
            this._pill.destroy();
            this._pill = null;
        }
    }
}
