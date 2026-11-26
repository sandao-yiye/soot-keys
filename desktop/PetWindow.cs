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
        readonly PetMenu menu = new PetMenu();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ToolStripMenuItem follow = new ToolStripMenuItem("整只猫跟随鼠标");
        readonly ToolStripMenuItem keyNames = new ToolStripMenuItem("显示按键名称");
        readonly ToolStripMenuItem pause = new ToolStripMenuItem("暂停键鼠互动");
        readonly bool smokeTest;
        readonly bool quiet;
        readonly PetSettings preferences;
        readonly CatToolbox toolbox;
        readonly ToolStripMenuItem[] levels = new ToolStripMenuItem[PetSettings.Sizes.Length];
        bool refreshing, nativeAligned;
        Point? initialNativeAnchor;
        readonly IntPtr scratch = Marshal.AllocHGlobal(64);
        readonly InputEdge[] edges = new InputEdge[10];
        Surface surface;
        PointF position;
        Point dragOffset;
        double lastTick;
        const double KeyNameSeconds = 1.0;
        string keyName;
        int captionKey;
        double keyNameUntil;
        bool listening, mouseListening, grabbed, dragging, dirty = true;
        Point pressPoint;
        int paints;
        Pose lastPose;
        int lastFrame;
        int lastActionKey;
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
            keyNames.CheckOnClick = true;
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
            menu.Items.Add("碎碎念", null, delegate { toolbox.Quip(this); });
            menu.Items.Add("墨爪百宝箱", null, delegate { toolbox.Open(this); });
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
            menu.Items.Add(keyNames);
            menu.Items.Add(pause);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(linked ? "暂时收起墨爪" : "退出墨爪", null, delegate { Close(); });
            pause.CheckedChanged += delegate
            {
                if (refreshing) return;
                preferences.Paused = pause.Checked;
                SetListening(!pause.Checked);
                SavePreferences();
            };
            follow.CheckedChanged += delegate
            {
                if (refreshing) return;
                preferences.Follow = follow.Checked;
                SavePreferences();
            };
            keyNames.CheckedChanged += delegate
            {
                if (refreshing) return;
                preferences.ShowKeyName = keyNames.Checked;
                if (!keyNames.Checked) { ClearKeyName(); motion.ClearLabels(); }
                else RestoreHeldCaption();
                SavePreferences();
            };
            RefreshMenu();
            tray.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            tray.Text = "墨爪 · 右键设置，拖动小猫换位置";
            ContextMenuStrip = menu;
            tray.ContextMenuStrip = menu;
            menu.Opened += delegate { SetMouseListening(true); };
            menu.Closed += delegate { SetMouseListening(listening); };
            MouseDown += OnPetDown;
            MouseMove += OnPetMove;
            MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (grabbed && e.Button == MouseButtons.Left)
                {
                    if (dragging) { preferences.Anchor = AnchorPoint(); SavePreferences(); }
                    grabbed = dragging = false;
                    Capture = false;
                }
            };
            MouseCaptureChanged += delegate { if (!Capture) grabbed = dragging = false; };
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
            keyNames.Checked = preferences.ShowKeyName;
            if (!preferences.ShowKeyName) { ClearKeyName(); motion.ClearLabels(); }
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
            if (enabled != listening)
            {
                Native.Listen(Handle, 6, enabled);
                listening = enabled;
                if (!enabled)
                {
                    motion.Reset(clock.Elapsed.TotalSeconds);
                    ClearKeyName();
                    dirty = true;
                }
            }
            SetMouseListening(enabled || menu.Visible);
        }

        void SetMouseListening(bool enabled)
        {
            if (enabled == mouseListening) return;
            Native.Listen(Handle, 2, enabled);
            mouseListening = enabled;
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
                if (menu.Visible) { menu.Close(ToolStripDropDownCloseReason.AppClicked); return; }
                pressPoint = PointToScreen(e.Location);
                dragOffset = e.Location;
                grabbed = true;
                Capture = true;
            }
        }

        void OnPetMove(object sender, MouseEventArgs e)
        {
            if (!grabbed) return;
            Point p = Cursor.Position;
            if (!dragging)
            {
                Size threshold = SystemInformation.DragSize;
                if (Math.Abs(p.X-pressPoint.X) < threshold.Width/2 && Math.Abs(p.Y-pressPoint.Y) < threshold.Height/2) return;
                dragging = true;
                nativeAligned = true;
                follow.Checked = false;
            }
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
            if (keyName != null && now >= keyNameUntil) ClearKeyName();
            double elapsed = Math.Min(.1, Math.Max(0, now-lastTick));
            lastTick = now;
            Point cursor = listening ? Cursor.Position : Point.Empty;
            Size size = surface.Size;
            PointF oldPosition = position;
            if (follow.Checked && listening && !grabbed && !menu.Visible)
                FollowPointer(cursor, elapsed);
            int frame;
            Pose pose = motion.Frame(now, out frame);
            if (listening) motion.Look(cursor.X-position.X-size.Width*.50, cursor.Y-position.Y-size.Height*.32, elapsed);
            float gx = (float)Math.Round(motion.GazeX*80)/80, gy = (float)Math.Round(motion.GazeY*80)/80;
            float blink = motion.Blink(now), right = motion.RightLid(now, blink);
            int actionKey = motion.ActionKeyId;
            InputEdge heldKey = motion.HeldKey;
            int mouseButtons = motion.HeldMouseButtons;
            if (pose == Pose.Mouse && mouseButtons != 0 && heldKey.KeyId != 0)
            {
                // The mouse paw stays down while the keyboard paw keeps its own target.
                pose = heldKey.Pose;
                frame = 3;
                actionKey = heldKey.KeyId;
            }
            bool contact = heldKey.KeyId != 0 && heldKey.KeyId == actionKey;
            if (contact && captionKey != heldKey.KeyId) RestoreHeldCaption();
            bool repaint = dirty || actionKey != lastActionKey || pose != lastPose || frame != lastFrame || gx != lastX || gy != lastY
                           || blink != lastLeft || right != lastRight;
            if (repaint)
            {
                art.Paint(surface.Canvas, size, pose, frame, gx, gy, blink, right,
                          keyName, actionKey, mouseButtons, contact, captionKey);
                paints++;
            }
            if (repaint || Point.Round(position) != Point.Round(oldPosition))
                surface.Present(Handle, Point.Round(position));
            dirty = false;
            lastActionKey = actionKey;
            lastPose = pose; lastFrame = frame; lastX = gx; lastY = gy;
            lastLeft = blink; lastRight = right;
            if (smokeTest && now >= .9)
            {
                timer.Stop();
                if (paints < 2) throw new InvalidOperationException("Layered window did not animate.");
                MenuSmoke.Verify(this, menu, levels, preferences, pause);
                toolbox.VerifyPopup(this);
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
                VerifyHeldCaptions();
                Close();
            }
        }

        void ClearKeyName()
        {
            if (keyName == null) return;
            keyName = null;
            captionKey = 0;
            dirty = true;
        }

        void VerifyHeldCaptions()
        {
            IntPtr first = new IntPtr(1), second = new IntPtr(2);
            var left = new InputEdge(first, 0x1d, Pose.Left, true, "ctrl");
            var right = new InputEdge(second, 0xe01d, Pose.Right, true, "ctrl");
            ProcessInput(left, Point.Empty);
            ProcessInput(right, Point.Empty);
            if (keyName != "ctrl" || captionKey != right.KeyId || !double.IsPositiveInfinity(keyNameUntil))
                throw new InvalidOperationException("Held right ctrl needs its own anchor and a persistent caption.");
            System.Threading.Thread.Sleep(180); // Hold past the contact part of a tap.
            ProcessInput(new InputEdge(second, right.KeyId, Pose.Right, false, null), Point.Empty);
            if (captionKey != left.KeyId || !double.IsPositiveInfinity(keyNameUntil))
                throw new InvalidOperationException("Releasing right ctrl lost the still-held left ctrl.");
            ProcessInput(new InputEdge(first, left.KeyId, Pose.Left, false, null), Point.Empty);
            if (keyName != "ctrl" || double.IsPositiveInfinity(keyNameUntil))
                throw new InvalidOperationException("Releasing the last key needs to start caption expiry.");
            keyNames.PerformClick();
            ProcessInput(new InputEdge(first, left.KeyId, Pose.Left, true, null), Point.Empty);
            if (keyName != null || preferences.ShowKeyName) throw new InvalidOperationException("Key caption toggle failed.");
            keyNames.PerformClick();
            if (keyName != "ctrl" || captionKey != left.KeyId)
                throw new InvalidOperationException("Re-enabling captions did not restore the still-held key.");
            pause.Checked = true;
            if (keyName != null || motion.HeldKey.KeyId != 0 || motion.HeldMouseButtons != 0)
                throw new InvalidOperationException("Pausing left held inputs visible.");
            pause.Checked = false;
        }

        void SetKeyName(InputEdge key, double until)
        {
            if (String.IsNullOrEmpty(key.Label)) { ClearKeyName(); return; }
            dirty |= keyName != key.Label || captionKey != key.KeyId;
            keyName = key.Label;
            captionKey = key.KeyId;
            keyNameUntil = until;
        }

        void RestoreHeldCaption()
        {
            InputEdge key = motion.HeldKey;
            if (!preferences.ShowKeyName || key.KeyId == 0) return;
            if (String.IsNullOrEmpty(key.Label))
                key = new InputEdge(key.Device, key.KeyId, key.Pose, true, KeyboardKey.NameForId(key.KeyId));
            SetKeyName(key, double.PositiveInfinity);
        }

        internal void ProcessInput(InputEdge edge, Point screen)
        {
            if (edge.Down && edge.KeyId == InputEdge.FirstMouse) menu.DismissOutside(screen);
            if (!listening) return;
            double now = clock.Elapsed.TotalSeconds;
            if (!motion.Apply(edge, now)) return;
            dirty = true;
            if (!preferences.ShowKeyName || edge.Pose == Pose.Mouse) return;
            if (edge.Down)
                SetKeyName(edge, motion.HeldKey.KeyId == edge.KeyId ? double.PositiveInfinity : now + KeyNameSeconds);
            else if (motion.HeldKey.KeyId != 0 && (motion.HeldKey.KeyId == captionKey || motion.ActionKeyId != captionKey))
                RestoreHeldCaption();
            else if (keyName != null) keyNameUntil = now + KeyNameSeconds;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x007b) // WM_CONTEXTMENU includes the originating screen point.
            {
                long packed = m.LParam.ToInt64();
                Point point = packed == -1 ? PointToScreen(new Point(Width / 2, Height / 2)) :
                    new Point(unchecked((short)packed), unchecked((short)(packed >> 16)));
                menu.Prepare(point);
            }
            if (m.Msg == Native.WmInput && (listening || mouseListening))
            {
                int count = Native.ReadInput(m.LParam, scratch, listening && preferences.ShowKeyName, edges);
                for (int i = 0; i < count; i++)
                {
                    InputEdge edge = edges[i];
                    edges[i] = default(InputEdge);
                    ProcessInput(edge, edge.Pose == Pose.Mouse && edge.Down ? Native.MessagePoint() : Point.Empty);
                }
            }
            if (m.Msg == 0x00fe && m.WParam.ToInt64() == 2 && listening) // WM_INPUT_DEVICE_CHANGE, GIDC_REMOVAL
            {
                InputEdge previous = motion.HeldKey;
                motion.RemoveDevice(m.LParam, clock.Elapsed.TotalSeconds);
                if (previous.KeyId != 0 && previous.Device == m.LParam)
                {
                    ClearKeyName();
                    RestoreHeldCaption();
                }
                dirty = true;
            }
            base.WndProc(ref m);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                timer.Dispose();
                toolbox.Dispose();
                if (listening || mouseListening)
                {
                    try
                    {
                        if (menu.Visible) menu.Close();
                        SetListening(false);
                    }
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
