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
        [DllImport("user32.dll")] static extern IntPtr GetCapture();
        [StructLayout(LayoutKind.Sequential)]
        struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Target; }
        [DllImport("user32.dll")] static extern uint GetRegisteredRawInputDevices([Out] Device[] devices, ref uint count, uint size);

        static bool Registered(IntPtr window, ushort usage)
        {
            uint count = 0, size = (uint)Marshal.SizeOf(typeof(Device));
            if (GetRegisteredRawInputDevices(null, ref count, size) == uint.MaxValue || count == 0) return false;
            Device[] devices = new Device[count];
            if (GetRegisteredRawInputDevices(devices, ref count, size) == uint.MaxValue) return false;
            return Array.Exists(devices, d => d.Page == 1 && d.Usage == usage && d.Target == window
                                            && (d.Flags & 0x130) == 0x100); // Input sink; legacy mouse messages remain enabled.
        }

        static void RawLeft(PetWindow pet, Point screen)
        {
            IntPtr packet = Marshal.AllocHGlobal(64);
            try
            {
                Marshal.Copy(new byte[64], 0, packet, 64);
                Marshal.WriteInt16(packet, 8 + 2 * IntPtr.Size + 4, 1); // RIM_TYPEMOUSE, RI_MOUSE_LEFT_BUTTON_DOWN
                pet.ProcessInput(Native.Decode(packet, 64), screen);
            }
            finally { Marshal.FreeHGlobal(packet); }
        }

        sealed class ClickTarget : Form
        {
            protected override bool ShowWithoutActivation { get { return true; } }
        }

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

        internal static void Verify(PetWindow pet, ContextMenuStrip menu, ToolStripMenuItem[] levels,
                                    PetSettings preferences, ToolStripMenuItem pause)
        {
            Point body = new Point(pet.ClientSize.Width/2, pet.ClientSize.Height/2);
            Point screen = pet.PointToScreen(body);
            int style = GetWindowLong(pet.Handle, -20);
            Require((style & 0x08080020) == 0x08080000, "expected a layered, non-activating, mouse-interactive window");
            Require(WindowFromPoint(screen) == pet.Handle, "opaque cat body did not receive native hit testing at " + screen);
            Require(WindowFromPoint(pet.PointToScreen(Point.Empty)) != pet.Handle, "transparent corner intercepted input");
            Click(pet, body, true);
            Require(menu.Visible && IsWindowVisible(menu.Handle), "right button release did not leave a visible menu");
            Require(GetCapture() == IntPtr.Zero, "menu captured input meant for other windows");
            // A dropdown may flip upward or sideways near a display edge.
            Rectangle near = menu.Bounds;
            near.Inflate(3,3);
            Require(near.Contains(screen), "menu opened at " + menu.Bounds + " instead of requested point " + screen);
            ToolStripDropDownItem sizes = levels[0].OwnerItem as ToolStripDropDownItem;
            Rectangle work = Screen.FromPoint(screen).WorkingArea;
            Require(work.Contains(menu.Bounds), "menu extends outside the work area");
            Require(menu.Font.Unit == GraphicsUnit.Pixel && menu.Font.Size >= Math.Min(18, work.Height/30f)
                    && sizes.Height >= 1.7f*menu.Font.Size, "menu text or click target is too small");
            Point row = Center(sizes.Bounds);
            Require(WindowFromPoint(menu.PointToScreen(row)) == menu.Handle, "size row was obscured");
            RawLeft(pet, menu.PointToScreen(row));
            Require(menu.Visible, "left press inside main menu cancelled it");
            Click(menu, row, false);
            ToolStripDropDown sub = sizes.DropDown;
            Require(sub.Visible && IsWindowVisible(sub.Handle), "click on size row failed to open submenu");
            Require(GetCapture() == IntPtr.Zero, "submenu captured input meant for other windows");
            Point largest = Center(levels[4].Bounds);
            Require(WindowFromPoint(sub.PointToScreen(largest)) == sub.Handle, "size option was obscured");
            Require(Screen.FromControl(sub).WorkingArea.Contains(sub.Bounds) && levels[4].Height == sizes.Height
                    && levels[4].Font.Size == sizes.Font.Size, "submenu failed size or screen-fit check");
            RawLeft(pet, sub.PointToScreen(largest));
            Require(menu.Visible && sub.Visible, "left press inside submenu cancelled it");
            Click(sub, largest, false);
            Require(preferences.Level == 4 && !menu.Visible, "click on largest option did not apply and close menu");
            SendMessage(pet.Handle, 0x007b, pet.Handle, new IntPtr(-1)); // WM_CONTEXTMENU from the keyboard
            Application.DoEvents();
            Require(menu.Visible, "keyboard context-menu request failed");
            Click(pet, body, false);
            Require(!menu.Visible, "left click on cat did not dismiss its menu");
            using (var other = new ClickTarget { FormBorderStyle = FormBorderStyle.None, ShowInTaskbar = false,
                                                StartPosition = FormStartPosition.Manual, Size = new Size(48,48),
                                                Location = new Point(work.Left + 8, work.Top + 8) })
            {
                int clicks = 0;
                other.MouseDown += delegate { clicks++; };
                other.Show();
                Point local = new Point(20,20);
                foreach (bool paused in new[] { false, true })
                {
                    pause.Checked = paused;
                    Click(pet, body, true);
                    Require(menu.Visible && Registered(pet.Handle, 2), "menu did not register background mouse delivery");
                    Require(Registered(pet.Handle, 6) != paused, "paused menu changed keyboard registration");
                    Point outside = other.PointToScreen(local);
                    Require(!menu.Bounds.Contains(outside), "outside target overlapped the menu");
                    RawLeft(pet, outside);
                    Require(!menu.Visible, "outside left button did not dismiss menu");
                    Require(Registered(pet.Handle, 2) != paused, "paused menu left mouse registered after dismissal");
                    Click(other, local, false);
                }
                Require(clicks == 2, "outside window did not receive left click");
                pause.Checked = false;
            }
        }
    }
}
