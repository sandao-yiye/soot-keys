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
                case Keys.ShiftKey: return scan == 0x36 ? "右 Shift" : "左 Shift";
                case Keys.LShiftKey: return "左 Shift";
                case Keys.RShiftKey: return "右 Shift";
                case Keys.ControlKey: return extended ? "右 Ctrl" : "左 Ctrl";
                case Keys.LControlKey: return "左 Ctrl";
                case Keys.RControlKey: return "右 Ctrl";
                case Keys.Menu: return extended ? "右 Alt" : "左 Alt";
                case Keys.LMenu: return "左 Alt";
                case Keys.RMenu: return "右 Alt";
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
                case Keys.LWin: return "左 Win";
                case Keys.RWin: return "右 Win";
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
                Marshal.WriteInt32(packet, 1); // RIM_TYPEKEYBOARD
                string label;
                if (scanA != 0 && Name((int)scanA, 0, (int)Keys.A, layout) != "A")
                    throw new InvalidOperationException("Keyboard layout letter mapping failed.");
                foreach (var sample in new[] {
                    new { Scan=0x39, Flags=0, Key=Keys.Space,     Label="空格" },
                    new { Scan=0x1c, Flags=0, Key=Keys.Return,    Label="回车" },
                    new { Scan=0x1c, Flags=2, Key=Keys.Return,    Label="Num 回车" },
                    new { Scan=0x4b, Flags=0, Key=Keys.Left,      Label="Num 4" },
                    new { Scan=0x4b, Flags=2, Key=Keys.Left,      Label="←" },
                    new { Scan=0x1d, Flags=2, Key=Keys.ControlKey,Label="右 Ctrl" },
                    new { Scan=0x36, Flags=0, Key=Keys.ShiftKey,  Label="右 Shift" },
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
                }
            }
            finally { Marshal.FreeHGlobal(packet); }
        }
    }
}
