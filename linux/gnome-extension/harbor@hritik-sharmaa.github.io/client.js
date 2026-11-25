// Harbor's GNOME extension ↔ harbord (Harbor's Linux helper, linux/harbord) over the D-Bus session bus.
// The first call starts harbord through D-Bus activation. If harbord isn't installed, every call fails
// quietly and `available` stays false: the dock then runs on GNOME's own favourites.

import Gio from 'gi://Gio';

const BUS_NAME = 'io.github.hritik_sharmaa.Harbor';
const OBJECT_PATH = '/io/github/hritik_sharmaa/Harbor';

const HarborIface = `
<node>
  <interface name="io.github.hritik_sharmaa.Harbor1">
    <method name="Version"><arg type="s" direction="out" name="version"/></method>
    <method name="Get"><arg type="s" direction="in" name="section"/><arg type="s" direction="out" name="json"/></method>
    <method name="Call"><arg type="s" direction="in" name="action"/><arg type="s" direction="in" name="args"/><arg type="s" direction="out" name="json"/></method>
    <signal name="Changed"><arg type="s" name="section"/></signal>
    <signal name="Activity"><arg type="s" name="json"/></signal>
    <signal name="OpenTab"><arg type="s" name="tab"/></signal>
  </interface>
</node>`;

const HarborProxy = Gio.DBusProxy.makeProxyWrapper(HarborIface);

export class HarborClient {
    constructor() {
        this.available = false;
        this._proxy = null;
        this._signals = [];
        this._handlers = {changed: [], activity: [], openTab: [], available: []};
        this._destroyed = false;
    }

    /** Connects (starting harbord if it's installed). Resolves either way; check `available`. */
    async start() {
        try {
            this._proxy = await new Promise((resolve, reject) => {
                // eslint-disable-next-line no-new
                new HarborProxy(Gio.DBus.session, BUS_NAME, OBJECT_PATH, (proxy, error) => {
                    if (error) reject(error); else resolve(proxy);
                }, null, Gio.DBusProxyFlags.NONE);
            });
        } catch (e) {
            this._log(`can't create the D-Bus proxy: ${e.message}`);
            return;
        }
        if (this._destroyed) return;
        this._signals.push(this._proxy.connectSignal('Changed', (_p, _s, [section]) => this._emit('changed', section)));
        this._signals.push(this._proxy.connectSignal('Activity', (_p, _s, [json]) => this._emit('activity', parse(json))));
        this._signals.push(this._proxy.connectSignal('OpenTab', (_p, _s, [tab]) => this._emit('openTab', tab)));
        // harbord restarting (an update, a crash): pick everything up again once it's back
        this._ownerId = this._proxy.connect('notify::g-name-owner', () => {
            const was = this.available;
            this.available = !!this._proxy.g_name_owner;
            if (this.available && !was) this._hello();
            if (this.available !== was) this._emit('available', this.available);
        });
        await this._hello();
    }

    async _hello() {
        try {
            const [version] = await this._proxy.VersionAsync();
            this.available = true;
            this._log(`connected to harbord ${version}`);
            await this.call('extension.hello');
            this._emit('available', true);
        } catch (e) {
            this.available = false;
            // ServiceUnknown: harbord isn't installed. Anything else is worth a line in the journal.
            if (!String(e.message).includes('ServiceUnknown')) this._log(`harbord isn't answering: ${e.message}`);
        }
    }

    /** Parsed JSON state of a section ("dock", "reminders", "all"...), or null. */
    async get(section) {
        if (!this._proxy || !this.available) return null;
        try {
            const [json] = await this._proxy.GetAsync(section);
            return parse(json);
        } catch (e) {
            this._log(`Get(${section}) failed: ${e.message}`);
            return null;
        }
    }

    /** Runs an action ("dock.pin", {id}) and returns harbord's JSON reply, or null. */
    async call(action, args = {}) {
        if (!this._proxy || !this.available) return null;
        try {
            const [json] = await this._proxy.CallAsync(action, JSON.stringify(args));
            return parse(json);
        } catch (e) {
            this._log(`Call(${action}) failed: ${e.message}`);
            return null;
        }
    }

    /** on('changed' | 'activity' | 'openTab' | 'available', fn) */
    on(what, fn) {
        this._handlers[what].push(fn);
    }

    _emit(what, value) {
        for (const fn of this._handlers[what]) {
            try {
                fn(value);
            } catch (e) {
                logError(e, `Harbor: ${what} handler`);
            }
        }
    }

    _log(msg) {
        console.log(`Harbor: ${msg}`);
    }

    destroy() {
        this._destroyed = true;
        if (this._proxy) {
            // tell harbord nobody draws live activities now (don't wait for the reply)
            if (this.available) this._proxy.CallRemote('extension.bye', '{}', () => {});
            for (const id of this._signals) this._proxy.disconnectSignal(id);
            if (this._ownerId) this._proxy.disconnect(this._ownerId);
        }
        this._signals = [];
        this._proxy = null;
        this._handlers = {changed: [], activity: [], openTab: [], available: []};
    }
}

function parse(json) {
    try {
        return JSON.parse(json);
    } catch {
        return null;
    }
}

