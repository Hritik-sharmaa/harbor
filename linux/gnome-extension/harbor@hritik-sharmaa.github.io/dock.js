// Harbor's dock for GNOME Shell: pinned and running apps, running dots, magnification that follows the pointer,
// a launch bounce, hover labels, a right-click menu and auto-hide. Pins and looks come from harbord when it's
// installed (the same settings.ini model as on Windows); without it the dock shows GNOME's favourites.

import Clutter from 'gi://Clutter';
import GLib from 'gi://GLib';
import Meta from 'gi://Meta';
import Shell from 'gi://Shell';
import St from 'gi://St';

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import * as PopupMenu from 'resource:///org/gnome/shell/ui/popupMenu.js';
import * as AppFavorites from 'resource:///org/gnome/shell/ui/appFavorites.js';

const DEFAULTS = {
    iconSize: 48, magnify: true, magnifyPercent: 150, autoHide: false, showRunning: true, dots: true, bounce: true,
    spacing: 6, padH: 8, padV: 6, radius: 20, margin: 6, borderWidth: 1,
    fill: null, border: null, pinned: null, hidden: [],
};

const HIDE_DELAY_MS = 500;

/** GNOME's own favourites (the Dash's pinned apps), as desktop IDs. */
function favoriteIds() {
    return AppFavorites.getAppFavorites().getFavorites().map(app => app.get_id());
}
const ANIM_MS = 180;

export class Dock {
    constructor(client) {
        this._client = client;
        this._cfg = {...DEFAULTS};
        this._items = [];          // {app, actor, icon, dot, pinned}
        this._pointerX = null;     // set while the pointer is over the dock (magnification)
        this._hidden = false;
        this._hideTimeout = 0;
        this._rebuildIdle = 0;
        this._signals = [];
        this._appSystem = Shell.AppSystem.get_default();
        this._tracker = Shell.WindowTracker.get_default();

        // the dock itself (floats above windows, never reserves space: it changes size while magnifying)
        this.actor = new St.BoxLayout({
            name: 'harborDock', style_class: 'harbor-dock', reactive: true, track_hover: true,
            x_align: Clutter.ActorAlign.CENTER, y_align: Clutter.ActorAlign.END,
        });
        this.actor.connect('motion-event', (_a, ev) => {
            [this._pointerX] = ev.get_coords();
            this._magnify();
            return Clutter.EVENT_PROPAGATE;
        });
        this.actor.connect('leave-event', () => {
            this._pointerX = null;
            this._magnify();
            this._scheduleHide();
            return Clutter.EVENT_PROPAGATE;
        });
        this.actor.connect('enter-event', (_a, ev) => {
            this._cancelHide();
            // magnify straight away: a quick flick into the dock may bring no motion event at all
            [this._pointerX] = ev.get_coords();
            this._magnify();
            return Clutter.EVENT_PROPAGATE;
        });
        this.actor.connect('notify::width', () => this._place());
        this.actor.connect('notify::height', () => this._place());
        Main.layoutManager.addChrome(this.actor, {affectsStruts: false, trackFullscreen: true});

        // an invisible strip the size of the resting dock: maximized windows stop above it
        this._strut = new St.Widget({name: 'harborDockStrut', reactive: false, opacity: 0});
        Main.layoutManager.addChrome(this._strut, {affectsStruts: true, trackFullscreen: true});

        // a thin edge along the bottom that brings an auto-hidden dock back
        this._edge = new St.Widget({name: 'harborDockEdge', reactive: true, opacity: 0});
        this._edge.connect('enter-event', () => this._show());
        Main.layoutManager.addChrome(this._edge, {affectsStruts: false, trackFullscreen: true});

        this._menuManager = new PopupMenu.PopupMenuManager(this.actor);

        this._connect(this._appSystem, 'installed-changed', () => this._queueRebuild());
        this._connect(this._appSystem, 'app-state-changed', () => this._queueRebuild());
        this._connect(AppFavorites.getAppFavorites(), 'changed', () => { if (!this._client.available) this._queueRebuild(); });
        this._connect(global.display, 'notify::focus-window', () => this._updateFocus());
        this._connect(global.display, 'window-demands-attention', (_d, w) => this._bounceFor(w));
        this._connect(global.display, 'window-marked-urgent', (_d, w) => this._bounceFor(w));
        this._connect(Main.layoutManager, 'monitors-changed', () => this._place());
        this._connect(Main.overview, 'showing', () => this.actor.hide());
        this._connect(Main.overview, 'hidden', () => { this.actor.show(); this._place(); });
        // windows moving over the dock: Smart Hide (only while auto-hide is on). Each window reports its own moves.
        this._windows = new Map();
        this._smartIdle = 0;
        this._connect(global.display, 'window-created', (_d, w) => this._trackWindow(w));
        this._connect(global.display, 'restacked', () => this._queueSmartHide());
        this._connect(global.workspace_manager, 'active-workspace-changed', () => this._queueSmartHide());
        for (const a of global.get_window_actors()) this._trackWindow(a.meta_window);

        client.on('changed', section => { if (section === 'dock') this.reload(); });
        client.on('available', () => this.reload());
        this.reload();
    }

    _connect(obj, signal, fn) {
        this._signals.push([obj, obj.connect(signal, fn)]);
    }

    // ------------------------------------------------------------------ settings and items

    /** Reads the dock's settings from harbord (or the defaults) and rebuilds. */
    async reload() {
        const s = this._client.available ? await this._client.get('dock') : null;
        if (!this.actor) return; // destroyed while waiting
        this._cfg = {...DEFAULTS};
        if (s) {
            for (const k of Object.keys(DEFAULTS)) if (s[k] !== undefined && s[k] !== null) this._cfg[k] = s[k];
            this._cfg.pinned = s.pinned ?? null;
        }
        this._applyStyle();
        this._rebuild();
        if (this._cfg.autoHide) this._smartHide();
        else this._show();
    }

    _pinnedIds() {
        // harbord's pins, or GNOME's favourites when harbord isn't there (or has none yet)
        if (this._client.available && Array.isArray(this._cfg.pinned) && this._cfg.pinned.length > 0) return this._cfg.pinned;
        return favoriteIds();
    }

    _queueRebuild() {
        if (this._rebuildIdle) return;
        this._rebuildIdle = GLib.idle_add(GLib.PRIORITY_DEFAULT_IDLE, () => {
            this._rebuildIdle = 0;
            this._rebuild();
            return GLib.SOURCE_REMOVE;
        });
    }

    _rebuild() {
        if (!this.actor) return;
        // detach first: destroying an item under the pointer sends a leave-event that would touch it again
        const old = this._items;
        this._items = [];
        for (const it of old) it.actor.destroy();
        const seen = new Set();
        const hidden = new Set(this._cfg.hidden || []);
        for (const id of this._pinnedIds()) {
            const app = this._appSystem.lookup_app(id);
            if (!app || seen.has(app.get_id())) continue;
            seen.add(app.get_id());
            this._addItem(app, true);
        }
        if (this._cfg.showRunning) {
            const running = this._appSystem.get_running().filter(a => !seen.has(a.get_id()) && !hidden.has(a.get_id()));
            if (running.length > 0 && this._items.length > 0) {
                const sep = new St.Widget({style_class: 'harbor-dock-separator', y_expand: true, y_align: Clutter.ActorAlign.CENTER});
                this.actor.add_child(sep);
                this._items.push({actor: sep, separator: true});
            }
            for (const app of running) this._addItem(app, false);
        }
        this._updateFocus();
        this._magnify();
        this._place();
    }

    _addItem(app, pinned) {
        const size = this._cfg.iconSize;
        const box = new St.BoxLayout({vertical: true, style_class: 'harbor-dock-item', reactive: true, track_hover: true, y_align: Clutter.ActorAlign.END});
        const icon = app.create_icon_texture(size);
        icon.set_pivot_point(0.5, 1);
        const dot = new St.Widget({style_class: 'harbor-dock-dot', x_align: Clutter.ActorAlign.CENTER});
        box.add_child(icon);
        box.add_child(dot);
        const label = new St.Label({style_class: 'harbor-dock-label', text: app.get_name()});
        label.hide();
        Main.layoutManager.addTopChrome(label);
        const item = {app, actor: box, icon, dot, label, pinned};

        box.connect('notify::hover', () => this._label(item, box.hover));
        box.connect('button-release-event', (_a, ev) => {
            const b = ev.get_button();
            if (b === Clutter.BUTTON_PRIMARY) this._activate(item);
            else if (b === Clutter.BUTTON_MIDDLE) this._launch(item, true);
            else if (b === Clutter.BUTTON_SECONDARY) this._menu(item);
            return Clutter.EVENT_STOP;
        });
        box.connect('destroy', () => label.destroy());
        this.actor.add_child(box);
        this._items.push(item);
        this._updateDot(item);
    }

    _updateDot(item) {
        const running = item.app.get_state() !== Shell.AppState.STOPPED;
        item.dot.opacity = running && this._cfg.dots ? 255 : 0;
    }

    _updateFocus() {
        const focused = this._tracker.focus_app;
        for (const it of this._items) {
            if (it.separator) continue;
            this._updateDot(it);
            if (it.app === focused) it.actor.add_style_pseudo_class('focused');
            else it.actor.remove_style_pseudo_class('focused');
        }
    }

    // ------------------------------------------------------------------ clicks

    _activate(item) {
        const app = item.app;
        const windows = app.get_windows().filter(w => !w.skip_taskbar);
        if (windows.length === 0) {
            this._launch(item, false);
            return;
        }
        if (this._tracker.focus_app === app) {
            // focused: cycle through its windows, or tuck a single one away (like the Windows dock)
            if (windows.length > 1) Main.activateWindow(windows[windows.length - 1]);
            else windows[0].minimize();
            return;
        }
        Main.activateWindow(windows[0]);
    }

    _launch(item, newWindow) {
        if (newWindow && item.app.can_open_new_window()) item.app.open_new_window(-1);
        else item.app.activate();
        if (this._cfg.bounce) this._bounce(item, 2);
    }

    _bounceFor(window) {
        const app = this._tracker.get_window_app(window);
        const item = this._items.find(it => it.app === app);
        if (item && this._cfg.bounce && !window.has_focus()) this._bounce(item, 3);
    }

    _bounce(item, times) {
        const h = this._cfg.iconSize * 0.45;
        const up = n => {
            if (n <= 0 || !item.icon) return;
            item.icon.ease({
                translation_y: -h, duration: 220, mode: Clutter.AnimationMode.EASE_OUT_QUAD,
                onComplete: () => item.icon?.ease({
                    translation_y: 0, duration: 260, mode: Clutter.AnimationMode.EASE_OUT_BOUNCE,
                    onComplete: () => up(n - 1),
                }),
            });
        };
        up(times);
    }

    _menu(item) {
        if (this._openMenu) this._openMenu.destroy();
        const menu = new PopupMenu.PopupMenu(item.actor, 0.5, St.Side.BOTTOM);
        this._openMenu = menu;
        menu.actor.add_style_class_name('harbor-dock-menu');
        Main.uiGroup.add_child(menu.actor);
        this._menuManager.addMenu(menu);
        const app = item.app;
        for (const w of app.get_windows().filter(x => !x.skip_taskbar))
            menu.addAction(w.get_title() || app.get_name(), () => Main.activateWindow(w));
        if (app.get_n_windows() > 0) menu.addMenuItem(new PopupMenu.PopupSeparatorMenuItem());
        if (app.can_open_new_window()) menu.addAction('New Window', () => this._launch(item, true));
        const pinned = this._pinnedIds().includes(app.get_id());
        menu.addAction(pinned ? 'Remove from Dock' : 'Keep in Dock', () => this._setPinned(app, !pinned));
        if (app.get_state() !== Shell.AppState.STOPPED) menu.addAction('Quit', () => app.request_quit());
        menu.connect('open-state-changed', (_m, open) => {
            if (!open) GLib.idle_add(GLib.PRIORITY_DEFAULT_IDLE, () => { menu.destroy(); if (this._openMenu === menu) this._openMenu = null; return GLib.SOURCE_REMOVE; });
        });
        menu.open(true);
    }

    async _setPinned(app, pin) {
        const id = app.get_id();
        if (this._client.available) {
            // first pin through harbord: start from what the dock shows now (GNOME's favourites)
            if (!Array.isArray(this._cfg.pinned) || this._cfg.pinned.length === 0)
                for (const f of favoriteIds()) if (f !== id) await this._client.call('dock.pin', {id: f});
            await this._client.call(pin ? 'dock.pin' : 'dock.unpin', {id});
        } else {
            const fav = AppFavorites.getAppFavorites();
            if (pin) fav.addFavorite(id); else fav.removeFavorite(id);
        }
    }

    _label(item, show) {
        if (!show) {
            item.label.hide();
            return;
        }
        const [x, y] = item.actor.get_transformed_position();
        const [w] = item.actor.get_transformed_size();
        item.label.show();
        item.label.set_position(Math.round(x + w / 2 - item.label.width / 2), Math.round(y - item.label.height - 8));
    }

    // ------------------------------------------------------------------ magnification

    /** Icon sizes from the pointer's distance to each icon's resting centre (so sizes don't feed back on themselves). */
    _magnify() {
        if (!this.actor) return;
        const base = this._cfg.iconSize;
        const max = this._cfg.magnify ? Math.round(base * this._cfg.magnifyPercent / 100) : base;
        const icons = this._items.filter(it => !it.separator);
        const step = base + this._cfg.spacing;
        const restingWidth = icons.length * step;
        const monitor = Main.layoutManager.primaryMonitor;
        const left = monitor ? monitor.x + (monitor.width - restingWidth) / 2 : 0;
        const range = base * 2.6;
        icons.forEach((it, i) => {
            let s = base;
            if (this._pointerX !== null && max > base) {
                const d = Math.abs(this._pointerX - (left + i * step + step / 2));
                if (d < range) s = base + (max - base) * (1 + Math.cos(Math.PI * d / range)) / 2;
            }
            it.icon.set_icon_size(Math.round(s));
        });
    }

    // ------------------------------------------------------------------ placement and hiding

    _applyStyle() {
        const c = this._cfg;
        const fill = c.fill ?? 'rgba(30,30,34,0.45)';
        const border = c.border ?? 'rgba(255,255,255,0.18)';
        this.actor.set_style(`background-color: ${fill}; border: ${c.borderWidth}px solid ${border}; border-radius: ${c.radius}px; ` +
            `padding: ${c.padV}px ${c.padH}px 2px ${c.padH}px; spacing: ${c.spacing}px;`);
    }

    _place() {
        const m = Main.layoutManager.primaryMonitor;
        if (!m || !this.actor) return;
        const [, natH] = this.actor.get_preferred_height(-1);
        const [, natW] = this.actor.get_preferred_width(-1);
        const margin = this._cfg.margin;
        const y = this._hidden ? m.y + m.height - 1 : m.y + m.height - natH - margin;
        this.actor.set_position(Math.round(m.x + (m.width - natW) / 2), Math.round(y));
        // the strut: resting height, full width of the dock; none while auto-hiding
        const restH = this._cfg.iconSize + this._cfg.padV + 2 + 6 + 2 * this._cfg.borderWidth + margin;
        if (this._cfg.autoHide) this._strut.set_size(0, 0);
        else {
            this._strut.set_size(Math.round(natW), restH);
            this._strut.set_position(Math.round(m.x + (m.width - natW) / 2), m.y + m.height - restH);
        }
        this._edge.set_size(m.width, 2);
        this._edge.set_position(m.x, m.y + m.height - 2);
        this._edge.visible = this._cfg.autoHide;
    }

    _trackWindow(w) {
        if (!w || this._windows.has(w)) return;
        const ids = ['position-changed', 'size-changed', 'notify::minimized'].map(sig => w.connect(sig, () => this._queueSmartHide()));
        ids.push(w.connect('unmanaged', () => this._untrackWindow(w)));
        this._windows.set(w, ids);
        this._queueSmartHide();
    }

    _untrackWindow(w) {
        const ids = this._windows.get(w);
        if (!ids) return;
        for (const id of ids) w.disconnect(id);
        this._windows.delete(w);
        this._queueSmartHide();
    }

    /** At most one overlap check per idle cycle, however many windows moved. */
    _queueSmartHide() {
        if (this._smartIdle || !this._cfg.autoHide) return;
        this._smartIdle = GLib.idle_add(GLib.PRIORITY_DEFAULT_IDLE, () => {
            this._smartIdle = 0;
            this._smartHide();
            return GLib.SOURCE_REMOVE;
        });
    }

    _smartHide() {
        if (!this.actor || !this._cfg.autoHide || this.actor.hover) return;
        if (this._windowOverlaps()) this._scheduleHide();
        else this._show();
    }

    _windowOverlaps() {
        const [x, y] = this.actor.get_transformed_position();
        const [w, h] = this.actor.get_transformed_size();
        const ws = global.workspace_manager.get_active_workspace();
        return global.display.get_tab_list(Meta.TabList.NORMAL, ws).some(win => {
            if (win.minimized) return false;
            const r = win.get_frame_rect();
            return r.x < x + w && r.x + r.width > x && r.y < y + h && r.y + r.height > y - 4;
        });
    }

    _scheduleHide() {
        if (!this.actor || !this._cfg.autoHide) return;
        this._cancelHide();
        this._hideTimeout = GLib.timeout_add(GLib.PRIORITY_DEFAULT, HIDE_DELAY_MS, () => {
            this._hideTimeout = 0;
            if (!this.actor.hover && this._windowOverlaps()) this._setHidden(true);
            return GLib.SOURCE_REMOVE;
        });
    }

    _cancelHide() {
        if (this._hideTimeout) GLib.source_remove(this._hideTimeout);
        this._hideTimeout = 0;
    }

    _show() {
        this._cancelHide();
        this._setHidden(false);
    }

    _setHidden(hidden) {
        if (this._hidden === hidden) return;
        this._hidden = hidden;
        const m = Main.layoutManager.primaryMonitor;
        const [, natH] = this.actor.get_preferred_height(-1);
        const y = hidden ? m.y + m.height - 1 : m.y + m.height - natH - this._cfg.margin;
        this.actor.ease({y, duration: ANIM_MS, mode: Clutter.AnimationMode.EASE_OUT_QUAD});
    }

    destroy() {
        const items = this._items;
        this._items = [];
        this._cancelHide();
        if (this._smartIdle) GLib.source_remove(this._smartIdle);
        this._smartIdle = 0;
        for (const w of [...this._windows.keys()]) this._untrackWindow(w);
        if (this._rebuildIdle) GLib.source_remove(this._rebuildIdle);
        this._rebuildIdle = 0;
        for (const [obj, id] of this._signals) obj.disconnect(id);
        this._signals = [];
        if (this._openMenu) this._openMenu.destroy();
        this._openMenu = null;
        this._menuManager = null;
        for (const it of items) it.actor.destroy();
        const actors = [this._edge, this._strut, this.actor];
        this._edge = this._strut = this.actor = null;
        for (const a of actors) {
            Main.layoutManager.removeChrome(a);
            a.destroy();
        }
    }
}
