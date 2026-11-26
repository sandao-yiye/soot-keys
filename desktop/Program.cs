using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Soot Keys")]
[assembly: AssemblyDescription("A four-pawed keyboard companion")]
[assembly: AssemblyProduct("Soot Keys")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace SootKeys
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            bool test = args.Length == 1 && args[0] == "--self-test";
            bool smoke = args.Length == 1 && args[0] == "--smoke-test";
            try
            {
                using (Illustration art = new Illustration())
                {
                    if (test) { Verify(art); return 0; }
                    Native.DpiAware();
                    Application.EnableVisualStyles();
                    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                    bool first;
                    using (Mutex mutex = new Mutex(true, smoke ? null : @"Local\SootKeys.LiveCat.1", out first))
                    {
                        if (!first) return 0;
                        using (PetWindow window = new PetWindow(art, smoke))
                            Application.Run(window);
                    }
                }
                return 0;
            }
            catch (Exception error)
            {
                if (test || smoke)
                    Console.Error.WriteLine(error.ToString());
                else
                    MessageBox.Show(error.Message, "Soot Keys", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static void Check(bool success, string subject)
        {
            if (!success) throw new InvalidOperationException("Self-test failed: " + subject);
        }

        static void Verify(Illustration art)
        {
            Motion cat = new Motion(42);
            int frame;
            // Exercise the same Win32 packet decoder as WM_INPUT without sending input to other apps.
            int headerBytes = 8 + IntPtr.Size * 2;
            IntPtr packet = Marshal.AllocHGlobal(64);
            try
            {
                Marshal.Copy(new byte[64], 0, packet, 64);
                Check(Native.Decode(packet, 64) == Pose.Idle, "mouse motion packet");
                Marshal.WriteInt16(packet, headerBytes + 4, 1);
                Check(Native.Decode(packet, 64) == Pose.Mouse, "left button down packet");
                Marshal.WriteInt32(packet, 1);
                Marshal.WriteInt16(packet, headerBytes, 0x39);
                Check(Native.Decode(packet, 64) == Pose.Space, "keyboard make packet");
                Marshal.WriteInt16(packet, headerBytes + 2, 1);
                Check(Native.Decode(packet, 64) == Pose.Idle, "keyboard break packet");
                Check(Native.Decode(packet, 10) == Pose.Idle, "truncated packet");
            }
            finally { Marshal.FreeHGlobal(packet); }
            Check(Native.KeyboardPose(0x39, 0) == Pose.Space && Native.KeyboardPose(0x39, 1) == Pose.Idle, "space down/up");
            Check(Native.KeyboardPose(0x1c, 2) == Pose.Enter, "extended Enter");
            Check(Native.KeyboardPose(0x1e, 0) == Pose.Left && Native.KeyboardPose(0x26, 0) == Pose.Right, "keyboard sides");
            cat.Strike(Pose.Space, .1);
            Check(cat.Frame(.16, out frame) == Pose.Space && frame == 2, "hind paw contact");
            cat.Strike(Pose.Left, .17);
            Check(cat.Frame(.44, out frame) == Pose.Left, "queued typing");
            cat.Reset(1);
            for (int i=0; i<6; i++) cat.Strike(i%2==0 ? Pose.Left : Pose.Right, 1 + .08*i);
            Check(cat.Frame(1.45, out frame) == Pose.Combo, "typing streak");
            Check(cat.Frame(2, out frame) == Pose.Idle, "idle recovery");
            cat.Look(200, -10, .04);
            Check(cat.GazeX > 0 && cat.GazeY < 0, "mouse tracking");
            bool shut = false;
            for (int tick=0; tick<700; tick++) shut |= cat.Blink(1+tick*.01) < .10f;
            Check(shut, "natural blink");
            using (Bitmap bitmap = new Bitmap(192, 208, PixelFormat.Format32bppPArgb))
            using (Graphics canvas = Graphics.FromImage(bitmap))
            {
                foreach (Pose p in Enum.GetValues(typeof(Pose)))
                for (int f=0; f<6; f++)
                {
                    art.Paint(canvas, bitmap.Size, p, f, 0, 0, 1, 1);
                    Check(bitmap.GetPixel(0,0).A == 0, "transparent corner");
                    int opaque = 0;
                    for (int y=0; y<208; y+=8)
                    for (int x=0; x<192; x+=8) if (bitmap.GetPixel(x,y).A > 128) opaque++;
                    Check(opaque > 80, "visible frame " + p + "-" + f);
                }
                VerifyGaze(art, canvas, bitmap, true);
                VerifyGaze(art, canvas, bitmap, false);
            }
        }

        static void VerifyGaze(Illustration art, Graphics canvas, Bitmap bitmap, bool vertical)
        {
            art.Paint(canvas, bitmap.Size, Pose.Idle, 0, vertical ? 0 : -1, vertical ? -1 : 0, 1, 1);
            using (Bitmap before = new Bitmap(bitmap))
            {
                art.Paint(canvas, bitmap.Size, Pose.Idle, 0, vertical ? 0 : 1, vertical ? 1 : 0, 1, 1);
                int pupil = Color.FromArgb(26, 24, 35).ToArgb();
                int gone = 0, added = 0, goneSum = 0, addedSum = 0;
                for (int y=0; y<bitmap.Height; y++)
                for (int x=0; x<bitmap.Width; x++)
                {
                    bool oldPupil = before.GetPixel(x,y).ToArgb() == pupil;
                    bool newPupil = bitmap.GetPixel(x,y).ToArgb() == pupil;
                    int axis = vertical ? y : x;
                    if (oldPupil && !newPupil) { gone++; goneSum += axis; }
                    if (newPupil && !oldPupil) { added++; addedSum += axis; }
                }
                Check(gone > 12 && added > 12 && (double)addedSum/added - (double)goneSum/gone >= 8,
                      vertical ? "visible up/down gaze travel" : "visible left/right gaze travel");
            }
        }
    }
}
