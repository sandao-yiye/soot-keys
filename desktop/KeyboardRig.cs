using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;

namespace SootKeys
{
    // The artwork generator exports the projected keycaps and the endpoints of
    // each limb, in the same 192 x 208 coordinate system as the body frames.
    internal sealed class KeyboardRig
    {
        const int MouseBase = 0x20000;
        const int RearLeft = 0, RearRight = 1, FrontLeft = 2, FrontRight = 3;
        static readonly string[] limbNames = { "RL", "RR", "FL", "FR" };
        readonly Dictionary<int, RectangleF> caps = new Dictionary<int, RectangleF>();
        readonly Limb[,,] frames = new Limb[7, 6, 4];

        struct Limb
        {
            public PointF Root, End;
            public float Progress;
        }

        internal KeyboardRig(Func<string, Stream> open, string[] poses)
        {
            using (var reader = new StreamReader(open("keyboard.csv")))
            {
                reader.ReadLine();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] f = line.Split(',');
                    int id = f[0][0] == 'M' ? MouseBase + int.Parse(f[0].Substring(1), CultureInfo.InvariantCulture)
                                            : int.Parse(f[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    float w = Number(f[3]), h = Number(f[4]);
                    caps.Add(id, new RectangleF(Number(f[1])-w/2, Number(f[2])-h/2, w, h));
                }
            }
            using (var reader = new StreamReader(open("rig.csv")))
            {
                reader.ReadLine();
                int count = 0;
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] f = line.Split(',');
                    int p = Array.IndexOf(poses, f[0]), frame = int.Parse(f[1], CultureInfo.InvariantCulture);
                    int limb = Array.IndexOf(limbNames, f[2]);
                    frames[p, frame, limb] = new Limb {
                        Root = new PointF(Number(f[3]), Number(f[4])),
                        End = new PointF(Number(f[5]), Number(f[6])), Progress = Number(f[7])
                    };
                    count++;
                }
                if (count != frames.Length) throw new InvalidDataException("Incomplete limb animation.");
            }
        }

        static float Number(string text) { return float.Parse(text, CultureInfo.InvariantCulture); }

        internal RectangleF Bounds(int id)
        {
            RectangleF r;
            return caps.TryGetValue(id, out r) ? r : RectangleF.Empty;
        }

        static PointF ToeTarget(RectangleF cap)
        {
            // The bottom of the cream sock is four design pixels below the wrist.
            return new PointF(cap.X + cap.Width/2, cap.Y + cap.Height/2 - 4);
        }

        static PointF Mix(PointF from, PointF to, float amount)
        {
            return new PointF(from.X + (to.X-from.X)*amount, from.Y + (to.Y-from.Y)*amount);
        }

        static int DemoKey(Pose pose)
        {
            switch (pose)
            {
                case Pose.Left: return 0x1e;
                case Pose.Right: return 0x26;
                case Pose.Space: return 0x39;
                case Pose.Enter: return 0x1c;
                case Pose.Mouse: return MouseBase+3;
                default: return 0;
            }
        }

        static int StrikingLimb(Pose pose, int key, RectangleF cap)
        {
            if (pose == Pose.Space || key == 0x39) return RearRight;
            if (pose == Pose.Enter || key == 0x1c || key == 0xe01c) return RearLeft;
            if (pose == Pose.Left) return FrontRight;
            if (pose == Pose.Right) return FrontLeft;
            return cap.X + cap.Width/2 < 100 ? FrontLeft : FrontRight;
        }

        internal void Paint(Graphics g, Pose pose, int frame, int key, int buttons, bool keyContact)
        {
            int p = (int)pose, demo = DemoKey(pose);
            int action = key == 0 ? demo : key;
            RectangleF cap = Bounds(action);
            int keyLimb = -1, mouseId = 0;
            if (action > MouseBase && action <= MouseBase+5) mouseId = action;
            else if (!cap.IsEmpty && (pose != Pose.Idle || keyContact))
                keyLimb = StrikingLimb(pose, action, cap);

            for (int i = 0; i < 5; i++)
            {
                if ((buttons & (1 << i)) == 0) continue;
                if (mouseId == 0 || (buttons & (1 << (mouseId-MouseBase-1))) == 0) mouseId = MouseBase + i + 1;
                Highlight(g, Bounds(MouseBase+i+1));
            }
            if (mouseId != 0 && keyLimb == FrontLeft) keyLimb = FrontRight;

            float pressure = keyContact ? 1f : keyLimb >= 0 ? frames[p,frame,keyLimb].Progress : 0;
            if (keyLimb >= 0 && pressure >= .5f) Highlight(g, cap);
            for (int i = 0; i < 4; i++)
            {
                Limb limb = frames[p,frame,i];
                PointF end = limb.End;
                if (i == keyLimb)
                    end = Retarget(limb, cap, DefaultForLimb(pose,i,demo), pressure);
                else if (i == FrontLeft && mouseId != 0)
                    end = Retarget(limb, Bounds(mouseId), pose == Pose.Mouse ? MouseBase+3 : 0,
                                   buttons != 0 ? 1f : limb.Progress);
                DrawLimb(g, limb.Root, end, i == RearLeft || i == RearRight);
            }
        }

        static int DefaultForLimb(Pose pose, int limb, int demo)
        {
            if (pose == Pose.Combo)
            {
                switch (limb)
                {
                    case FrontRight: return 0x1e;
                    case FrontLeft: return 0x26;
                    case RearRight: return 0x39;
                    default: return 0x1c;
                }
            }
            return demo;
        }

        PointF Retarget(Limb limb, RectangleF cap, int demo, float amount)
        {
            if (cap.IsEmpty || amount <= 0) return limb.End;
            PointF target = ToeTarget(cap);
            if (amount >= .999f) return target;
            RectangleF original = Bounds(demo);
            if (original.IsEmpty) return Mix(limb.End, target, amount);
            PointF old = ToeTarget(original);
            return new PointF(limb.End.X+(target.X-old.X)*amount, limb.End.Y+(target.Y-old.Y)*amount);
        }

        static void Highlight(Graphics g, RectangleF cap)
        {
            if (cap.IsEmpty) return;
            using (var glow = new SolidBrush(Color.FromArgb(151, 255, 176, 70)))
            using (var rim = new Pen(Color.FromArgb(210, 158, 83, 45), .9f))
            {
                g.FillRectangle(glow, cap);
                g.DrawRectangle(rim, cap.X, cap.Y, cap.Width, cap.Height);
            }
        }

        // The curved wool sleeve and white sock follow the same silhouette as
        // the sprite generator's limb(), drawn between the projected joints.
        static void DrawLimb(Graphics g, PointF root, PointF end, bool rear)
        {
            float sx = root.X, sy = root.Y, x = end.X, y = end.Y;
            float vx = x-sx, vy = y-sy;
            float length = Math.Max(1f, (float)Math.Sqrt(vx*vx+vy*vy));
            float width = rear ? 6.59f : 5.52f;
            float nx = -vy/length*width, ny = vx/length*width, bend = sx < 97 ? 2.7f : -2.7f;
            PointF a = new PointF(sx+nx, sy+ny), b = new PointF(x+nx*.8f, y+ny*.8f);
            PointF c = new PointF(x-nx*.8f, y-ny*.8f), d = new PointF(sx-nx, sy-ny);
            using (var sleeve = new GraphicsPath())
            using (var fur = new SolidBrush(rear ? Color.FromArgb(37,38,48) : Color.FromArgb(48,48,60)))
            using (var edge = new Pen(Color.FromArgb(28,29,40), 1.2f))
            {
                Curve(sleeve, a, sx+vx*.4f+nx+bend, sy+vy*.44f+ny, b);
                Curve(sleeve, b, x+vx/length*4.5f, y+vy/length*4.5f, c);
                Curve(sleeve, c, sx+vx*.4f-nx+bend, sy+vy*.44f-ny, d);
                Curve(sleeve, d, sx-vx/length*6.2f, sy-vy/length*6.2f, a);
                sleeve.CloseFigure();
                g.FillPath(fur, sleeve);
                g.DrawPath(edge, sleeve);
            }
            float r = rear ? 6.23f : 5.7f;
            a = new PointF(x-r, y-1.8f);
            b = new PointF(x-r+1.7f, y-4.4f);
            c = new PointF(x+r-.9f, y-4.7f);
            d = new PointF(x+r-.27f, y+3.1f);
            using (var sock = new GraphicsPath())
            using (var wool = new SolidBrush(Color.FromArgb(255,241,216)))
            using (var seam = new Pen(Color.FromArgb(75,68,72), 1f))
            using (var toes = new Pen(Color.FromArgb(190,167,148), .7f))
            {
                Curve(sock,a,x-r,y-3.9f,b);
                Curve(sock,b,x,y-2.9f,c);
                Curve(sock,c,x+r+2.5f,y,d);
                sock.AddBezier(d.X,d.Y,x+r-2,y+6.3f,x-r+1.6f,y+6.7f,x-r,y+2.8f);
                sock.CloseFigure();
                g.FillPath(wool,sock);
                g.DrawPath(seam,sock);
                g.DrawLine(toes,x-1.8f,y+4.3f,x-1.1f,y+2.25f);
                g.DrawLine(toes,x+1.2f,y+4.3f,x+1.9f,y+2.25f);
            }
        }

        static void Curve(GraphicsPath path, PointF a, float cx, float cy, PointF b)
        {
            path.AddBezier(a.X, a.Y, (a.X+2*cx)/3, (a.Y+2*cy)/3,
                           (b.X+2*cx)/3, (b.Y+2*cy)/3, b.X, b.Y);
        }
    }
}
