// Harbor for GNOME Shell (45+). The dock and live activities live here; reminders, timers, Pomodoro, Focus,
// weather, calendar and Claude usage run in harbord, Harbor's helper, which D-Bus starts on first use.

import * as Main from 'resource:///org/gnome/shell/ui/main.js';
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

import {HarborClient} from './client.js';
import {Dock} from './dock.js';
import {LiveActivities} from './activity.js';

// other docks that would sit on top of ours
const OTHER_DOCKS = ['ubuntu-dock@ubuntu.com', 'dash-to-dock@micxgx.gmail.com', 'dash-to-panel@jderose9.github.com'];

export default class HarborExtension extends Extension {
    enable() {
        this._client = new HarborClient();
        this._dock = new Dock(this._client);
        this._activities = new LiveActivities(this._client);
        this._client.start().catch(e => logError(e, 'Harbor: starting the harbord client'));
        this._warnAboutOtherDocks();
    }

    disable() {
        this._activities?.destroy();
        this._dock?.destroy();
        this._client?.destroy();
        this._activities = this._dock = this._client = null;
    }

    _warnAboutOtherDocks() {
        const active = OTHER_DOCKS.filter(uuid => Main.extensionManager.lookup(uuid)?.state === 1 /* ENABLED / ACTIVE */);
        if (active.length === 0) return;
        Main.notify('Harbor', `Another dock is on (${active.join(', ')}). Turn it off in Extensions so it doesn't cover Harbor's dock.`);
    }
}
