using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace SootKeys
{
    internal static class KeyboardKey
    {
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);
        [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint threadId);
        [DllImport("user32.dll")] static extern uint MapVirtualKeyExW(uint code, uint mapType, IntPtr layout);
        [DllImport("user32.dll", CharSet=CharSet.Unicode, ExactSpelling=true)]
        static extern int GetKeyNameTextW(int keyData, StringBuilder name, int length);

        internal static IntPtr ForegroundLayout()
        {
            return GetKeyboardLayout(GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero));
        }

        // These are physical keypad positions. Windows changes the reported virtual key
        // when Num Lock changes; the label printed on the pet should stay recognizable.
        static readonly string[] Pad = {
            "Num 7", "Num 8", "Num 9", "Num −", "Num 4", "Num 5", "Num 6",
            "Num +", "Num 1", "Num 2", "Num 3", "Num 0", "Num ·"
        };

        internal static int PhysicalId(int scan, int flags, int vk)
        {
            // Pause arrives with a two-part E1 scan. Num Lock and Print Screen
            // have several wire encodings but only one physical key each.
            if (vk == (int)Keys.Pause || vk == (int)Keys.Cancel) return InputEdge.PauseKey;
            if (vk == (int)Keys.NumLock) return 0x45;
            if (vk == (int)Keys.PrintScreen) return 0xe037;
            if (scan == 0) return 0x10000 + vk;
            return scan | ((flags & 2) != 0 ? 0xe000 : (flags & 4) != 0 ? 0xe100 : 0);
        }

        // A key can already be held when the user switches captions back on.
        // Recreate its legend from its physical position and the active layout.
        internal static string NameForId(int id)
        {
            if (id == 0 || id >= InputEdge.FirstMouse) return null;
            IntPtr layout = ForegroundLayout();
            if (id >= 0x10000) return Name(0, 0, id - 0x10000, layout);
            if (id == InputEdge.PauseKey) return "Pause";
            int flags = (id & 0xff00) == 0xe000 ? 2 : (id & 0xff00) == 0xe100 ? 4 : 0;
            int vk = (int)MapVirtualKeyExW((uint)id, 3, layout);
            if (id == 0x45) vk = (int)Keys.NumLock;
            return Name(id & 0xff, flags, vk, layout);
        }

        internal static string Name(int scan, int flags, int vk, IntPtr layout)
        {
            bool extended = (flags & 6) != 0;
            if (!extended && scan >= 0x47 && scan <= 0x53) return Pad[scan - 0x47];
            if (extended && scan == 0x1c) return "Num 回车";

            uint fullScan = (uint)(scan | ((flags & 2) != 0 ? 0xe000 : (flags & 4) != 0 ? 0xe100 : 0));
            Keys mapped = scan == 0 ? Keys.None : (Keys)MapVirtualKeyExW(fullScan, 3, layout);
            Keys key = (Keys)vk;
            // Raw Input is delivered to a background window. Use the foreground layout
            // for letter/OEM positions (e.g. AZERTY and QWERTZ), and keep the packet's
            // explicit identity for media, IME and special keys such as Pause.
            if (key == Keys.None || key == Keys.ProcessKey || IsPrintablePosition(key))
            {
                if (mapped != Keys.None) key = mapped;
            }
            if (key >= Keys.F1 && key <= Keys.F24) return "F" + ((int)key - (int)Keys.F1 + 1);
            if (key >= Keys.NumPad0 && key <= Keys.NumPad9) return "Num " + ((int)key - (int)Keys.NumPad0);
            switch (key)
            {
                case Keys.Back: return "退格";
                case Keys.Tab: return "Tab";
                case Keys.Return: return "回车";
                case Keys.ShiftKey: case Keys.LShiftKey: case Keys.RShiftKey: return "shift";
                case Keys.ControlKey: case Keys.LControlKey: case Keys.RControlKey: return "ctrl";
                case Keys.Menu: case Keys.LMenu: case Keys.RMenu: return "alt";
                case Keys.Cancel:
                case Keys.Pause: return "Pause";
                case Keys.CapsLock: return "Caps Lock";
                case Keys.Escape: return "Esc";
                case Keys.Space: return "空格";
                case Keys.PageUp: return "PgUp";
                case Keys.PageDown: return "PgDn";
                case Keys.End: return "End";
                case Keys.Home: return "Home";
                case Keys.Left: return "←";
                case Keys.Up: return "↑";
                case Keys.Right: return "→";
                case Keys.Down: return "↓";
                case Keys.PrintScreen: return "PrtSc";
                case Keys.Insert: return "Ins";
                case Keys.Delete: return "Del";
                case Keys.LWin: case Keys.RWin: return "win";
                case Keys.Apps: return "菜单";
                case Keys.Multiply: return "Num ×";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num −";
                case Keys.Decimal: return "Num ·";
                case Keys.Divide: return "Num ÷";
                case Keys.NumLock: return "Num Lock";
                case Keys.Scroll: return "ScrLk";
                case Keys.VolumeMute: return "静音";
                case Keys.VolumeDown: return "音量 −";
                case Keys.VolumeUp: return "音量 +";
                case Keys.MediaNextTrack: return "下一曲";
                case Keys.MediaPreviousTrack: return "上一曲";
                case Keys.MediaStop: return "停止";
                case Keys.MediaPlayPause: return "播放/暂停";
            }
            // MAPVK_VK_TO_CHAR gives the unmodified key legend, including a dead key's
            // accent, without consulting modifier state or advancing the user's IME.
            char legend = (char)(MapVirtualKeyExW((uint)key, 2, layout) & 0xffff);
            if (!char.IsControl(legend) && !char.IsWhiteSpace(legend)) return legend.ToString();
            var name = new StringBuilder(64);
            if (scan != 0 && GetKeyNameTextW((scan << 16) | (extended ? 1 << 24 : 0), name, name.Capacity) > 0)
                return name.ToString();
            return Enum.GetName(typeof(Keys), key) ?? "VK " + vk.ToString("X2");
        }

        static bool IsPrintablePosition(Keys key)
        {
            return (key >= Keys.D0 && key <= Keys.Z) || (key >= Keys.Oem1 && key <= Keys.OemBackslash);
        }

        internal static void Verify()
        {
            IntPtr layout = GetKeyboardLayout(0);
            uint scanA = MapVirtualKeyExW((uint)Keys.A, 4, layout);
            int header = 8 + 2 * IntPtr.Size;
            IntPtr packet = Marshal.AllocHGlobal(64);
            try
            {
                Marshal.Copy(new byte[64], 0, packet, 64);
                var edges = new InputEdge[10];
                IntPtr device = new IntPtr(123);
                Marshal.WriteIntPtr(packet, 8, device);
                Marshal.WriteInt16(packet, header + 4, 0x209); // left down, right up, X2 up
                if (Native.DecodeInput(packet, 64, false, edges) != 3 ||
                    edges[0].Device != device || !edges[0].Down || edges[0].KeyId != InputEdge.FirstMouse ||
                    edges[1].Down || edges[1].KeyId != InputEdge.FirstMouse + 1 ||
                    edges[2].Down || edges[2].KeyId != InputEdge.FirstMouse + 4)
                    throw new InvalidOperationException("Grouped mouse button edges were lost.");
                Marshal.WriteInt32(packet, 1); // RIM_TYPEKEYBOARD
                string label;
                if (scanA != 0 && Name((int)scanA, 0, (int)Keys.A, layout) != "A")
                    throw new InvalidOperationException("Keyboard layout letter mapping failed.");
                if (NameForId(0x1d) != "ctrl" || NameForId(0xe01d) != "ctrl" || NameForId(0xe038) != "alt" ||
                    NameForId(0xe01c) != "Num 回车" || NameForId(InputEdge.FirstMouse) != null)
                    throw new InvalidOperationException("Failed to restore the held key caption.");
                foreach (var sample in new[] {
                    new { Scan=0x39, Flags=0, Key=Keys.Space,     Label="空格" },
                    new { Scan=0x1c, Flags=0, Key=Keys.Return,    Label="回车" },
                    new { Scan=0x1c, Flags=2, Key=Keys.Return,    Label="Num 回车" },
                    new { Scan=0x4b, Flags=0, Key=Keys.Left,      Label="Num 4" },
                    new { Scan=0x4b, Flags=2, Key=Keys.Left,      Label="←" },
                    new { Scan=0x1d, Flags=2, Key=Keys.ControlKey,Label="ctrl" },
                    new { Scan=0x1d, Flags=0, Key=Keys.ControlKey,Label="ctrl" },
                    new { Scan=0x38, Flags=0, Key=Keys.Menu,      Label="alt" },
                    new { Scan=0x38, Flags=2, Key=Keys.Menu,      Label="alt" },
                    new { Scan=0x36, Flags=0, Key=Keys.ShiftKey,  Label="shift" },
                    new { Scan=0x58, Flags=0, Key=Keys.F12,       Label="F12" },
                    new { Scan=0,    Flags=0, Key=Keys.VolumeUp,  Label="音量 +" }
                })
                {
                    Marshal.WriteInt16(packet, header, (short)sample.Scan);
                    Marshal.WriteInt16(packet, header + 2, (short)sample.Flags);
                    Marshal.WriteInt16(packet, header + 6, (short)sample.Key);
                    if (Native.Decode(packet, 64, true, out label) == Pose.Idle || label != sample.Label)
                        throw new InvalidOperationException("Missing key label for " + sample.Key + ".");
                    if (Native.Decode(packet, 64, false, out label) == Pose.Idle || label != null)
                        throw new InvalidOperationException("Hidden key labels changed the animation.");
                    Marshal.WriteInt16(packet, header + 2, (short)(sample.Flags | 1));
                    if (Native.Decode(packet, 64, true, out label) != Pose.Idle || label != null)
                        throw new InvalidOperationException("Key release produced a label.");
                    if (Native.DecodeInput(packet, 64, true, edges) != 1 || edges[0].Down || edges[0].Device != device ||
                        edges[0].KeyId != PhysicalId(sample.Scan, sample.Flags, (int)sample.Key) || edges[0].Label != null)
                        throw new InvalidOperationException("Keyboard break lost its physical identity.");
                }
            }
            finally { Marshal.FreeHGlobal(packet); }
        }
    }
}
