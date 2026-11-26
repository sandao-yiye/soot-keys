using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SootKeys
{
    // Use only the test process's own HWNDs. The system cursor is never moved.
    internal static class MenuSmoke
    {
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);

        static IntPtr Coordinates(Point p)
        {
            return new IntPtr(unchecked((int)((uint)(ushort)p.X | ((uint)(ushort)p.Y << 16))));
        }

        static Point Center(Rectangle r) { return new Point(r.Left+r.Width/2, r.Top+r.Height/2); }
        static void Require(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException("Context menu smoke test: " + what);
        }

        static void Click(Control target, Point p, bool right)
        {
            IntPtr where = Coordinates(p);
            SendMessage(target.Handle, 0x0200, IntPtr.Zero, where); // WM_MOUSEMOVE
            SendMessage(target.Handle, right ? 0x0204u : 0x0201u, new IntPtr(right ? 2 : 1), where);
            SendMessage(target.Handle, right ? 0x0205u : 0x0202u, IntPtr.Zero, where);
            Application.DoEvents();
        }

        internal static void Verify(PetWindow pet, ContextMenuStrip menu, ToolStripMenuItem[] levels, PetSettings preferences)
        {
            Point body = new Point(pet.ClientSize.Width/2, pet.ClientSize.Height/2);
            Point screen = pet.PointToScreen(body);
            int style = GetWindowLong(pet.Handle, -20);
            Require((style & 0x08080020) == 0x08080000, "expected a layered, non-activating, mouse-interactive window");
            Require(WindowFromPoint(screen) == pet.Handle, "opaque cat body did not receive native hit testing at " + screen);
            Require(WindowFromPoint(pet.PointToScreen(Point.Empty)) != pet.Handle, "transparent corner intercepted input");
            Click(pet, body, true);
            Require(menu.Visible && IsWindowVisible(menu.Handle), "right button release did not leave a visible menu");
            // A dropdown may flip upward or sideways near a display edge.
            Rectangle near = menu.Bounds;
            near.Inflate(3,3);
            Require(near.Contains(screen), "menu opened at " + menu.Bounds + " instead of requested point " + screen);
            ToolStripDropDownItem sizes = levels[0].OwnerItem as ToolStripDropDownItem;
            Point row = Center(sizes.Bounds);
            Require(WindowFromPoint(menu.PointToScreen(row)) == menu.Handle, "size row was obscured");
            Click(menu, row, false);
            ToolStripDropDown sub = sizes.DropDown;
            Require(sub.Visible && IsWindowVisible(sub.Handle), "click on size row failed to open submenu");
            Point largest = Center(levels[4].Bounds);
            Require(WindowFromPoint(sub.PointToScreen(largest)) == sub.Handle, "size option was obscured");
            Click(sub, largest, false);
            Require(preferences.Level == 4 && !menu.Visible, "click on largest option did not apply and close menu");
            SendMessage(pet.Handle, 0x007b, pet.Handle, new IntPtr(-1)); // WM_CONTEXTMENU from the keyboard
            Application.DoEvents();
            Require(menu.Visible, "keyboard context-menu request failed");
            menu.Close();
        }
    }
}
