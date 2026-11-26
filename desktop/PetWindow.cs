using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SootKeys
{
    internal sealed class PetWindow : Form
    {
        readonly Illustration art;
        readonly Motion motion = new Motion(Environment.TickCount);
        readonly Stopwatch clock = Stopwatch.StartNew();
        readonly Timer timer = new Timer { Interval = 20 };
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem follow = new ToolStripMenuItem("整只猫跟随鼠标");
        readonly ToolStripMenuItem pause = new ToolStripMenuItem("暂停键鼠互动");
        readonly bool smokeTest;
        readonly IntPtr scratch = Marshal.AllocHGlobal(64);
        Surface surface;
        PointF position;
        Point dragOffset;
        double lastTick;
        bool listening, dragging, dirty = true;
        int paints;
        Pose lastPose;
        int lastFrame;
        float lastX, lastY, lastLeft, lastRight;

        internal PetWindow(Illustration images, bool test)
        {
            art = images;
            smokeTest = test;
            Text = "Soot Keys · 墨爪";
            ShowInTaskbar = false;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            ClientSize = new Size(192, 208);
            Rectangle desk = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(desk.Right - Width - 24, desk.Bottom - Height - 20);
            follow.CheckOnClick = true;
            pause.CheckOnClick = true;
            menu.Items.Add(follow);
            menu.Items.Add(pause);
            menu.Items.Add("移回鼠标所在屏幕", null, delegate
            {
                Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
                position = new PointF(work.Right - surface.Size.Width - 24, work.Bottom - surface.Size.Height - 20);
                dirty = true;
            });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出墨爪", null, delegate { Close(); });
            pause.CheckedChanged += delegate
            {
                SetListening(!pause.Checked);
                motion.Reset(clock.Elapsed.TotalSeconds);
                dirty = true;
            };
            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            tray.Text = "墨爪 · 右键设置，拖动小猫换位置";
            tray.ContextMenuStrip = menu;
            MouseDown += OnPetDown;
            MouseMove += OnPetMove;
            MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) { dragging = false; Capture = false; }
            };
            MouseCaptureChanged += delegate { if (!Capture) dragging = false; };
            timer.Tick += delegate { Tick(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x08000000 | 0x80; // Layered, no-activate, tool window.
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            EnsureSurface();
            Rectangle desk = Screen.FromHandle(Handle).WorkingArea;
            position = new PointF(desk.Right - surface.Size.Width - 24, desk.Bottom - surface.Size.Height - 20);
            SetListening(true);
            tray.Visible = !smokeTest;
            Tick();
            timer.Start();
        }

        void SetListening(bool enabled)
        {
            if (enabled == listening) return;
            Native.Listen(Handle, enabled);
            listening = enabled;
        }

        void EnsureSurface()
        {
            uint dpi = 96;
            try { dpi = Math.Max(96u, Native.GetDpiForWindow(Handle)); }
            catch (EntryPointNotFoundException) { }
            Size size = new Size((int)(192*dpi/96), (int)(208*dpi/96));
            if (surface != null && size == surface.Size) return;
            if (surface != null) surface.Dispose();
            surface = new Surface(size);
            dirty = true;
        }

        void OnPetDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                menu.Show(Cursor.Position);
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                follow.Checked = false;
                dragOffset = e.Location;
                dragging = true;
                Capture = true;
            }
        }

        void OnPetMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            Point p = Cursor.Position;
            Rectangle desk = Screen.FromPoint(p).WorkingArea;
            position = new PointF(Clamp(p.X-dragOffset.X, desk.Left, desk.Right-surface.Size.Width),
                                  Clamp(p.Y-dragOffset.Y, desk.Top, desk.Bottom-surface.Size.Height));
            dirty = true;
        }

        static float Clamp(float x, float min, float max) { return Math.Max(min, Math.Min(max, x)); }
        static float Step(float current, float target, float rate)
        {
            float distance = target-current;
            return Math.Abs(distance) < .5f ? target : current + distance * rate;
        }

        void Tick()
        {
            EnsureSurface();
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = Math.Min(.1, Math.Max(0, now-lastTick));
            lastTick = now;
            Point cursor = listening ? Cursor.Position : Point.Empty;
            Size size = surface.Size;
            PointF oldPosition = position;
            if (follow.Checked && listening && !dragging && !menu.Visible)
            {
                Rectangle desk = Screen.FromPoint(cursor).WorkingArea;
                int gap = Math.Max(20, size.Width/8);
                int x = cursor.X + gap + size.Width <= desk.Right ? cursor.X+gap : cursor.X-size.Width-gap;
                int y = cursor.Y + gap + size.Height <= desk.Bottom ? cursor.Y+gap : cursor.Y-size.Height-gap;
                float rate = (float)(1-Math.Exp(-elapsed*9));
                position.X = Step(position.X, Clamp(x, desk.Left, desk.Right-size.Width), rate);
                position.Y = Step(position.Y, Clamp(y, desk.Top, desk.Bottom-size.Height), rate);
            }
            int frame;
            Pose pose = motion.Frame(now, out frame);
            if (listening) motion.Look(cursor.X-position.X-size.Width*.50, cursor.Y-position.Y-size.Height*.32, elapsed);
            float gx = (float)Math.Round(motion.GazeX*80)/80, gy = (float)Math.Round(motion.GazeY*80)/80;
            float blink = motion.Blink(now), right = motion.RightLid(now, blink);
            bool repaint = dirty || pose != lastPose || frame != lastFrame || gx != lastX || gy != lastY
                           || blink != lastLeft || right != lastRight;
            if (repaint)
            {
                art.Paint(surface.Canvas, size, pose, frame, gx, gy, blink, right);
                paints++;
            }
            if (repaint || Point.Round(position) != Point.Round(oldPosition))
                surface.Present(Handle, Point.Round(position));
            dirty = false;
            lastPose = pose; lastFrame = frame; lastX = gx; lastY = gy;
            lastLeft = blink; lastRight = right;
            if (smokeTest && now >= .9)
            {
                if (paints < 2) throw new InvalidOperationException("Layered window did not animate.");
                pause.Checked = true;
                pause.Checked = false;
                Close();
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WmInput && listening)
            {
                Pose pose = Native.ReadPose(m.LParam, scratch);
                if (pose != Pose.Idle) motion.Strike(pose, clock.Elapsed.TotalSeconds);
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                timer.Dispose();
                if (listening)
                {
                    try { SetListening(false); }
                    catch (Win32Exception) { } // Destroying the receiving window also unregisters its devices.
                }
                tray.Visible = false;
                if (tray.Icon != null) tray.Icon.Dispose();
                tray.Dispose();
                menu.Dispose();
                if (surface != null) surface.Dispose();
                Marshal.FreeHGlobal(scratch);
            }
            base.Dispose(disposing);
        }
    }
}
