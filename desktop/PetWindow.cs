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
        readonly bool quiet;
        readonly PetSettings preferences;
        readonly CatToolbox toolbox;
        readonly ToolStripMenuItem[] levels = new ToolStripMenuItem[PetSettings.Sizes.Length];
        bool refreshing, nativeAligned;
        Point? initialNativeAnchor;
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

        internal PetWindow(Illustration images, PetSettings settings, bool test, bool quiet = false, bool linked = false)
        {
            art = images;
            preferences = settings;
            smokeTest = test;
            this.quiet = quiet || test;
            toolbox = this.quiet ? new CatToolbox(Audition, settings.TestDirectory) : new CatToolbox(Audition);
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
            menu.Items.Add("打开 Codex", null, delegate
            {
                try
                {
                    using (Process launched = Process.Start(new ProcessStartInfo("codex://") { UseShellExecute = true })) { }
                }
                catch (Win32Exception)
                {
                    tray.ShowBalloonTip(3500, "墨爪", "未能打开 Codex。请确认已安装 Codex 桌面端。", ToolTipIcon.Info);
                }
            });
            menu.Items.Add("碎碎念", null, delegate { toolbox.Quip(); });
            menu.Items.Add("墨爪百宝箱", null, delegate { toolbox.Open(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("回到初始位置", null, delegate
            {
                nativeAligned = true;
                preferences.Anchor = null;
                follow.Checked = false;
                RestorePosition();
                SavePreferences();
            });
            ToolStripMenuItem sizeMenu = new ToolStripMenuItem("调整大小");
            for (int index = 0; index < levels.Length; index++)
            {
                int level = index;
                levels[index] = new ToolStripMenuItem(PetSettings.Sizes[index], null, delegate { ChangeSize(level); });
                sizeMenu.DropDownItems.Add(levels[index]);
            }
            sizeMenu.DropDownItems.Add(new ToolStripSeparator());
            sizeMenu.DropDownItems.Add("恢复标准大小", null, delegate { ChangeSize(PetSettings.Standard); });
            menu.Items.Add(sizeMenu);
            menu.Items.Add("重载配置", null, delegate
            {
                preferences.Reload();
                RefreshMenu();
                EnsureSurface();
                RestorePosition();
                SetListening(!preferences.Paused);
            });
            ToolStripMenuItem actions = new ToolStripMenuItem("动作");
            string[] actionNames = { "收爪待机", "左前爪拍键", "右前爪拍键", "左后脚踩空格",
                                     "右后脚够回车", "伸爪够鼠标", "四脚连击" };
            for (int i = 0; i < actionNames.Length; i++)
            {
                Pose selected = (Pose)i;
                actions.DropDownItems.Add(actionNames[i], null, delegate { Audition(selected); });
            }
            menu.Items.Add(actions);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(follow);
            menu.Items.Add(pause);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(linked ? "暂时收起墨爪" : "退出墨爪", null, delegate { Close(); });
            pause.CheckedChanged += delegate
            {
                if (refreshing) return;
                preferences.Paused = pause.Checked;
                SetListening(!pause.Checked);
                motion.Reset(clock.Elapsed.TotalSeconds);
                dirty = true;
                SavePreferences();
            };
            follow.CheckedChanged += delegate
            {
                if (refreshing) return;
                preferences.Follow = follow.Checked;
                SavePreferences();
            };
            RefreshMenu();
            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            tray.Text = "墨爪 · 右键设置，拖动小猫换位置";
            ContextMenuStrip = menu;
            tray.ContextMenuStrip = menu;
            MouseDown += OnPetDown;
            MouseMove += OnPetMove;
            MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (dragging && e.Button == MouseButtons.Left)
                {
                    dragging = false;
                    Capture = false;
                    preferences.Anchor = AnchorPoint();
                    SavePreferences();
                }
            };
            MouseCaptureChanged += delegate { if (!Capture) dragging = false; };
            timer.Tick += delegate { Tick(); };
        }

        void Audition(Pose pose)
        {
            motion.Audition(pose, clock.Elapsed.TotalSeconds);
            dirty = true;
        }

        void SavePreferences()
        {
            if (!preferences.Save())
                tray.ShowBalloonTip(2500, "墨爪", "本次设置已应用，本机设置文件暂时无法写入。", ToolTipIcon.Info);
        }

        void RefreshMenu()
        {
            refreshing = true;
            follow.Checked = preferences.Follow;
            pause.Checked = preferences.Paused;
            for (int i = 0; i < levels.Length; i++) levels[i].Checked = i == preferences.Level;
            refreshing = false;
        }

        void ChangeSize(int level)
        {
            preferences.Level = level;
            RefreshMenu();
            EnsureSurface();
            SavePreferences();
        }

        Point AnchorPoint()
        {
            return Point.Round(new PointF(position.X + surface.Size.Width / 2f, position.Y + surface.Size.Height));
        }

        void PlaceAt(Point anchor)
        {
            Rectangle desk = Screen.FromPoint(new Point(anchor.X, anchor.Y - 1)).WorkingArea;
            Size size = surface.Size;
            position = new PointF(Clamp(anchor.X - size.Width / 2f, desk.Left, desk.Right - size.Width),
                                  Clamp(anchor.Y - size.Height, desk.Top, desk.Bottom - size.Height));
            dirty = true;
        }

        void RestorePosition()
        {
            Rectangle desk = Screen.FromPoint(Cursor.Position).WorkingArea;
            PlaceAt(preferences.Anchor ?? new Point(desk.Right - surface.Size.Width / 2 - 24, desk.Bottom - 20));
        }

        internal void AlignToNative(Point? anchor)
        {
            if (nativeAligned || !anchor.HasValue || preferences.Anchor.HasValue ||
                preferences.Follow || dragging || menu.Visible) return;
            if (surface == null) { initialNativeAnchor = anchor; return; }
            PlaceAt(anchor.Value);
            nativeAligned = true;
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
            RestorePosition();
            AlignToNative(initialNativeAnchor);
            SetListening(!preferences.Paused);
            tray.Visible = !quiet;
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
            Size size = preferences.Pixels(dpi);
            if (surface != null && size == surface.Size) return;
            Point? anchor = surface == null ? (Point?)null : AnchorPoint();
            if (surface != null) surface.Dispose();
            surface = new Surface(size);
            if (anchor.HasValue) PlaceAt(anchor.Value);
            dirty = true;
        }

        void OnPetDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                nativeAligned = true;
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

        void FollowPointer(Point cursor, double elapsed)
        {
            Size size = surface.Size;
            int gap = Math.Max(20, size.Width/8);
            // Let the pointer reach a moving cat. Nearby it perches until the user leaves.
            Rectangle perch = new Rectangle(Point.Round(position), size);
            perch.Inflate(gap * 2, gap * 2);
            if (perch.Contains(cursor)) return;
            Rectangle desk = Screen.FromPoint(cursor).WorkingArea;
            int x = cursor.X + gap + size.Width <= desk.Right ? cursor.X+gap : cursor.X-size.Width-gap;
            int y = cursor.Y + gap + size.Height <= desk.Bottom ? cursor.Y+gap : cursor.Y-size.Height-gap;
            float rate = (float)(1-Math.Exp(-elapsed*9));
            position.X = Step(position.X, Clamp(x, desk.Left, desk.Right-size.Width), rate);
            position.Y = Step(position.Y, Clamp(y, desk.Top, desk.Bottom-size.Height), rate);
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
                FollowPointer(cursor, elapsed);
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
                timer.Stop();
                if (paints < 2) throw new InvalidOperationException("Layered window did not animate.");
                MenuSmoke.Verify(this, menu, levels, preferences);
                ChangeSize(PetSettings.Standard);
                Rectangle desk = Screen.FromHandle(Handle).WorkingArea;
                Point original = AnchorPoint();
                Point native = new Point(desk.Left + desk.Width/2, desk.Top + desk.Height/2);
                preferences.Anchor = original;
                AlignToNative(native);
                if (AnchorPoint() != original) throw new InvalidOperationException("Native position overrode a saved position.");
                preferences.Anchor = null;
                AlignToNative(native);
                AlignToNative(original);
                if (AnchorPoint() != native) throw new InvalidOperationException("Delayed native anchor was not applied exactly once.");
                PointF perched = position;
                FollowPointer(Point.Round(new PointF(position.X - 10, position.Y + surface.Size.Height / 2f)), .1);
                if (position != perched) throw new InvalidOperationException("Following cat moved away from an approaching pointer.");
                Size prior = surface.Size;
                Point anchor = AnchorPoint();
                for (int i = 0; i < levels.Length; i++)
                {
                    levels[i].PerformClick();
                    if (i == 0 && surface.Size.Width >= prior.Width || i > 0 && surface.Size.Width <= prior.Width)
                        throw new InvalidOperationException("Size menu did not update the pet.");
                    prior = surface.Size;
                }
                if (Math.Abs(AnchorPoint().Y - anchor.Y) > 1)
                    throw new InvalidOperationException("Resizing moved the bottom anchor.");
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
                toolbox.Dispose();
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
