// Test only (linux/tests/shell-smoke.sh): turns on GNOME Shell's unsafe mode so the test can use
// org.gnome.Shell.Eval and Screenshot from outside. Only ever loaded into a throwaway headless shell.
import {Extension} from 'resource:///org/gnome/shell/extensions/extension.js';

export default class UnsafeModeForTests extends Extension {
    enable() {
        global.context.unsafe_mode = true;
        console.log('HARBOR-TEST: unsafe mode on');
    }

    disable() {
        global.context.unsafe_mode = false;
    }
}
