using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SootKeys
{
    internal static class Native
    {
        internal const int WmInput = 0xff;
        [StructLayout(LayoutKind.Sequential)]
        struct Device { public ushort Page, Usage; public uint Flags; public IntPtr Window; }
        [StructLayout(LayoutKind.Sequential)]
        struct RawHeader { public uint Type, Size; public IntPtr Device, Param; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct Size2 { internal int Width, Height; }
        [StructLayout(LayoutKind.Sequential, Pack=1)]
        internal struct Blend { internal byte Operation, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct DibInfo
        {
            internal uint HeaderSize;
            internal int Width, Height;
            internal ushort Planes, Bits;
            internal uint Compression, ImageSize;
            internal int XPels, YPels;
            internal uint Colors, Important, Palette;
        }

        [DllImport("user32.dll", SetLastError=true)] static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError=true)] static extern uint GetRawInputData(IntPtr handle, uint command, IntPtr buffer, ref uint size, uint header);
        [DllImport("gdi32.dll", SetLastError=true)] internal static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll", SetLastError=true)] internal static extern IntPtr CreateDIBSection(IntPtr dc, ref DibInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] internal static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] internal static extern bool DeleteDC(IntPtr dc);
        [DllImport("user32.dll", SetLastError=true)]
        internal static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point dst, ref Size2 size, IntPtr src, ref Point zero, uint color, ref Blend blend, uint flags);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int mode);
        [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
        [DllImport("user32.dll")] static extern uint GetMessagePos();

        internal static uint MonitorDpi(Point point)
        {
            uint x, y;
            try { if (GetDpiForMonitor(MonitorFromPoint(point, 2), 0, out x, out y) == 0 && x > 0) return x; }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return 96;
        }

        internal static Point MessagePoint()
        {
            uint packed = GetMessagePos();
            return new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
        }

        internal static void DpiAware()
        {
            try { SetProcessDpiAwareness(2); }
            catch (EntryPointNotFoundException) { SetProcessDPIAware(); }
            catch (DllNotFoundException) { SetProcessDPIAware(); }
        }

        internal static void Listen(IntPtr window, ushort usage, bool enable)
        {
            Device device = new Device
            {
                Page = 1, Usage = usage, Flags = enable ? 0x100u : 1u,
                Window = enable ? window : IntPtr.Zero
            };
            if (!RegisterRawInputDevices(new[] { device }, 1, (uint)Marshal.SizeOf(typeof(Device))))
                throw new Win32Exception();
        }

        static readonly int HeaderBytes = Marshal.SizeOf(typeof(RawHeader));
        internal static Pose ReadPose(IntPtr packet, IntPtr scratch, bool showKeyName, out string keyLabel)
        {
            uint bytes = 64;
            uint received = GetRawInputData(packet, 0x10000003, scratch, ref bytes, (uint)HeaderBytes);
            return Decode(scratch, received, showKeyName, out keyLabel);
        }

        internal static Pose Decode(IntPtr scratch, uint received)
        {
            string ignored;
            return Decode(scratch, received, false, out ignored);
        }

        internal static Pose Decode(IntPtr scratch, uint received, bool showKeyName, out string keyLabel)
        {
            keyLabel = null;
            if (received == uint.MaxValue || received < HeaderBytes + 8) return Pose.Idle;
            int type = Marshal.ReadInt32(scratch);
            IntPtr data = IntPtr.Add(scratch, HeaderBytes);
            if (type == 0)
            {
                return (Marshal.ReadInt16(data, 4) & 1) != 0 ? Pose.Mouse : Pose.Idle;
            }
            if (type != 1 || received < HeaderBytes + 16) return Pose.Idle;
            int scan = Marshal.ReadInt16(data, 0) & 0xffff, flags = Marshal.ReadInt16(data, 2);
            int vk = Marshal.ReadInt16(data, 6) & 0xffff;
            if ((flags & 1) != 0 || scan == 0xff || vk >= 0xff || vk == 0xe7 ||
                ((flags & 2) != 0 && (scan == 0x2a || scan == 0x36))) return Pose.Idle;
            // Some multimedia buttons have a virtual key but no make code.
            Pose pose = scan == 0 && vk != 0 ? Pose.Right : KeyboardPose(scan, flags);
            if (showKeyName && pose != Pose.Idle)
                keyLabel = KeyboardKey.Name(scan, flags, vk, KeyboardKey.ForegroundLayout());
            return pose;
        }

        internal static Pose KeyboardPose(int scan, int flags)
        {
            if ((flags & 1) != 0 || scan == 0 || scan == 255) return Pose.Idle;
            if (scan == 0x39) return Pose.Space;
            if (scan == 0x1c) return Pose.Enter;
            if ((flags & 6) != 0) return Pose.Right;
            bool left = scan <= 6 || (scan >= 0xf && scan <= 0x14)
                || (scan >= 0x1d && scan <= 0x22) || (scan >= 0x2a && scan <= 0x2f)
                || scan == 0x38 || scan == 0x3a || (scan >= 0x3b && scan <= 0x3f);
            return left ? Pose.Left : Pose.Right;
        }
    }

    // GDI and GDI+ share one top-down premultiplied-alpha surface for the lifetime of the window.
    internal sealed class Surface : IDisposable
    {
        readonly IntPtr dc, dib, previous;
        readonly Bitmap bitmap;
        internal readonly Graphics Canvas;
        internal readonly Size Size;

        internal Surface(Size size)
        {
            Size = size;
            Native.DibInfo info = new Native.DibInfo();
            info.HeaderSize = 40;
            info.Width = size.Width;
            info.Height = -size.Height;
            info.Planes = 1;
            info.Bits = 32;
            dc = Native.CreateCompatibleDC(IntPtr.Zero);
            IntPtr bits;
            dib = Native.CreateDIBSection(dc, ref info, 0, out bits, IntPtr.Zero, 0);
            if (dc == IntPtr.Zero || dib == IntPtr.Zero)
            {
                if (dc != IntPtr.Zero) Native.DeleteDC(dc);
                if (dib != IntPtr.Zero) Native.DeleteObject(dib);
                throw new Win32Exception();
            }
            previous = Native.SelectObject(dc, dib);
            bitmap = new Bitmap(size.Width, size.Height, size.Width*4, PixelFormat.Format32bppPArgb, bits);
            Canvas = Graphics.FromImage(bitmap);
        }

        internal void Present(IntPtr window, Point destination)
        {
            Canvas.Flush(FlushIntention.Sync);
            Native.Size2 s = new Native.Size2 { Width = Size.Width, Height = Size.Height };
            Native.Blend b = new Native.Blend { Alpha = 255, Format = 1 };
            Point origin = Point.Empty;
            if (!Native.UpdateLayeredWindow(window, IntPtr.Zero, ref destination, ref s, dc, ref origin, 0, ref b, 2))
                throw new Win32Exception();
        }

        public void Dispose()
        {
            Canvas.Dispose();
            bitmap.Dispose();
            Native.SelectObject(dc, previous);
            Native.DeleteObject(dib);
            Native.DeleteDC(dc);
        }
    }
}
