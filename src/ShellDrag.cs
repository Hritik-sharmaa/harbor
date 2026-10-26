using System;
using System.Runtime.InteropServices;

namespace Harbor
{
    /// <summary>
    /// Drags files out of Harbor exactly like Explorer does: a shell data object (file list, shell items, drag image)
    /// handed to the shell's own drag loop. Browsers, chat apps, mail and folders all accept it.
    /// </summary>
    static class ShellDrag
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHParseDisplayName(string name, IntPtr bindCtx, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);
        [DllImport("shell32.dll")] static extern int SHCreateDataObject(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, IntPtr inner, ref Guid riid, out IntPtr obj);
        [DllImport("shell32.dll")] static extern int SHDoDragDrop(IntPtr hwnd, IntPtr data, IntPtr source, uint okEffects, out uint effect);
        [DllImport("shell32.dll")] static extern IntPtr ILClone(IntPtr pidl);
        [DllImport("shell32.dll")] static extern bool ILRemoveLastID(IntPtr pidl);
        [DllImport("shell32.dll")] static extern IntPtr ILFindLastID(IntPtr pidl);
        [DllImport("shell32.dll")] static extern void ILFree(IntPtr pidl);

        const uint Copy = 1, Move = 2, Link = 4;

        /// <summary>Shell data object for one file or folder (caller releases it), or IntPtr.Zero.</summary>
        public static IntPtr DataObjectFor(string path)
        {
            IntPtr full = IntPtr.Zero, parent = IntPtr.Zero, obj = IntPtr.Zero;
            try
            {
                uint attrs;
                if (SHParseDisplayName(path, IntPtr.Zero, out full, 0, out attrs) != 0 || full == IntPtr.Zero) return IntPtr.Zero;
                parent = ILClone(full);
                ILRemoveLastID(parent);
                IntPtr child = ILFindLastID(full); // points into 'full': the item relative to its folder
                Guid iid = new Guid("0000010e-0000-0000-C000-000000000046"); // IID_IDataObject
                if (SHCreateDataObject(parent, 1, new[] { child }, IntPtr.Zero, ref iid, out obj) != 0) obj = IntPtr.Zero;
                return obj;
            }
            catch (Exception ex) { App.Log(ex); return IntPtr.Zero; }
            finally
            {
                if (parent != IntPtr.Zero) ILFree(parent);
                if (full != IntPtr.Zero) ILFree(full);
            }
        }

        /// <summary>Runs the drag (blocks until the drop or cancel). Returns false if it couldn't start.</summary>
        public static bool Drag(IntPtr hwnd, string path)
        {
            IntPtr data = DataObjectFor(path);
            if (data == IntPtr.Zero) return false;
            try
            {
                uint effect;
                // copy by default (browsers, chat); Shift moves and Alt links when dropped on a folder, like Explorer
                SHDoDragDrop(hwnd, data, IntPtr.Zero, Copy | Move | Link, out effect);
                return true;
            }
            catch (Exception ex) { App.Log(ex); return false; }
            finally { Marshal.Release(data); }
        }
    }
}
