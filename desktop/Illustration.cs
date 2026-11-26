using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace SootKeys
{
    internal sealed class Illustration : IDisposable
    {
        readonly Bitmap[,] bodies = new Bitmap[7, 6];
        readonly RectangleF[,,] eyes = new RectangleF[7, 6, 2];
        readonly Font keyFont = new Font("Microsoft YaHei UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        static readonly string[] names = { "idle", "tapLeft", "tapRight", "space", "enter", "mouse", "combo" };
        static Stream Open(string file)
        {
            Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("cat." + file);
            if (stream == null) throw new InvalidDataException("Missing artwork: " + file);
            return stream;
        }

        internal Illustration()
        {
            using (StreamReader reader = new StreamReader(Open("eyes.csv")))
            {
                reader.ReadLine();
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Trim().Length == 0) continue;
                    string[] fields = line.Split(',');
                    int pose = Array.IndexOf(names, fields[0]);
                    int frame = int.Parse(fields[1], CultureInfo.InvariantCulture);
                    for (int i = 0; i < 2; i++)
                    {
                        int j = 2 + i * 4;
                        float x = Number(fields[j]), y = Number(fields[j+1]);
                        float rx = Number(fields[j+2]), ry = Number(fields[j+3]);
                        eyes[pose, frame, i] = new RectangleF(x-rx, y-ry, 2*rx, 2*ry);
                    }
                }
            }
            for (int p = 0; p < names.Length; p++)
            for (int f = 0; f < 6; f++)
            {
                if (eyes[p, f, 0].Width <= 0 || eyes[p, f, 1].Height <= 0)
                    throw new InvalidDataException("Missing eye coordinates for " + names[p]);
                using (Stream stream = Open(names[p] + "-" + f + ".png"))
                using (Bitmap original = new Bitmap(stream))
                    bodies[p,f] = original.Clone(new Rectangle(0, 0, original.Width, original.Height), PixelFormat.Format32bppPArgb);
            }
        }

        static float Number(string text) { return float.Parse(text, CultureInfo.InvariantCulture); }

        internal void Paint(Graphics g, Size size, Pose kind, int frame, float gx, float gy, float leftLid, float rightLid,
                            string keyLabel = null)
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            GraphicsState state = g.Save();
            g.ScaleTransform(size.Width / 192f, size.Height / 208f);
            int p = (int)kind;
            g.DrawImage(bodies[p, frame], 0, 0, 192, 208);
            Eye(g, eyes[p, frame, 0], gx, gy, leftLid);
            Eye(g, eyes[p, frame, 1], gx, gy, rightLid);
            if (!string.IsNullOrEmpty(keyLabel)) DrawKeycap(g, keyLabel);
            g.Restore(state);
        }

        void DrawKeycap(Graphics g, string label)
        {
            // Use the clear space above the illustrated mouse. Two-word labels
            // can stack on the same key so they don't cover the cat's front legs.
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                SizeF text = g.MeasureString(label, keyFont, PointF.Empty, format);
                if (text.Width > 52)
                {
                    int split = label.IndexOf(' ');
                    if (split > 0 && split < label.Length - 1)
                    {
                        label = label.Substring(0, split) + "\n" + label.Substring(split + 1);
                        text = g.MeasureString(label, keyFont, PointF.Empty, format);
                    }
                }
                // Fit uncommon long localized key names within the sprite as well.
                float scale = Math.Min(1f, 173f / Math.Max(1f, text.Width));
                float width = Math.Max(32, text.Width * scale + 10);
                float height = Math.Max(23, text.Height * scale + 2);
                float x = Math.Max(3, 26 - width / 2);
                float y = 152 - height;
                using (GraphicsPath baseShape = KeycapOutline(x, y + 1, width, height + 4))
                using (GraphicsPath face = KeycapOutline(x + 1.3f, y, width - 2.6f, height))
                using (Pen rim = new Pen(Color.FromArgb(255, 243, 218), 3.4f))
                using (Pen border = new Pen(Color.FromArgb(53, 42, 39), 1.45f))
                using (Brush wall = new SolidBrush(Color.FromArgb(224, 164, 85)))
                using (Brush cream = new SolidBrush(Color.FromArgb(255, 243, 216)))
                using (Brush ink = new SolidBrush(Color.FromArgb(53, 42, 39)))
                {
                    g.DrawPath(rim, baseShape);
                    g.FillPath(wall, baseShape);
                    g.DrawPath(border, baseShape);
                    g.FillPath(cream, face);
                    g.DrawPath(border, face);
                    g.TranslateTransform(x + width / 2, y + height / 2);
                    g.ScaleTransform(scale, scale);
                    g.DrawString(label, keyFont, ink, PointF.Empty, format);
                }
            }
        }

        static GraphicsPath KeycapOutline(float x, float y, float width, float height)
        {
            const float arc = 8;
            GraphicsPath shape = new GraphicsPath();
            shape.AddArc(x, y, arc, arc, 180, 90);
            shape.AddArc(x + width - arc, y, arc, arc, 270, 90);
            shape.AddArc(x + width - arc, y + height - arc, arc, arc, 0, 90);
            shape.AddArc(x, y + height - arc, arc, arc, 90, 90);
            shape.CloseFigure();
            return shape;
        }

        static void Eye(Graphics g, RectangleF socket, float gx, float gy, float openness)
        {
            float cx = socket.X + socket.Width / 2, cy = socket.Y + socket.Height / 2;
            float rx = socket.Width / 2, ry = socket.Height * openness / 2;
            RectangleF iris = new RectangleF(socket.X, cy - ry, socket.Width, 2 * ry);
            Color gold = Color.FromArgb(255, 189, 55);
            if (ry < 2)
            {
                using (Pen lid = new Pen(gold, 2f))
                    g.DrawArc(lid, socket.X, cy - 2, socket.Width, 3.6f, 8, 164);
                return;
            }
            using (SolidBrush amber = new SolidBrush(gold))
                g.FillEllipse(amber, iris);
            using (Pen outline = new Pen(Color.FromArgb(23, 24, 34), 1.05f))
                g.DrawEllipse(outline, iris);
            using (Pen rim = new Pen(Color.FromArgb(255, 219, 115), 1.35f))
                g.DrawBezier(rim, cx-rx*.78f, cy+ry*.36f, cx-rx*.45f, cy+ry*.87f,
                                  cx-rx*.01f, cy+ry*.97f, cx+rx*.52f, cy+ry*.68f);

            // Same normalized geometry as the sprite rig. Gaze is a unit vector.
            float px = cx + gx * rx * .50f, py = cy + gy * ry * .39f;
            float prx = rx * .30f, pry = ry * .55f;
            using (SolidBrush pupil = new SolidBrush(Color.FromArgb(26, 24, 35)))
                g.FillEllipse(pupil, px-prx, py-pry, 2*prx, 2*pry);
            float shine = Math.Min(1.2f, ry * .23f);
            using (SolidBrush glint = new SolidBrush(Color.FromArgb(255,242,198)))
                g.FillEllipse(glint, px-2.05f, py-ry*.25f-shine, 2.1f, 2*shine);
        }

        public void Dispose()
        {
            keyFont.Dispose();
            foreach (Bitmap frame in bodies) if (frame != null) frame.Dispose();
        }
    }
}
