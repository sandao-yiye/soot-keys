using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace SootKeys
{
    internal sealed class PetMenu : ContextMenuStrip
    {
        Font labelFont;
        float appliedScale;
        Point? requestedPoint;
        Point anchor;

        internal void Prepare(Point point)
        {
            requestedPoint = point;
            SizeFor(point); // WinForms chooses edge placement from the preferred size.
        }

        protected override void OnOpening(CancelEventArgs e)
        {
            anchor = requestedPoint ?? Cursor.Position;
            requestedPoint = null;
            SizeFor(anchor);
            base.OnOpening(e);
        }

        void SizeFor(Point point)
        {
            Rectangle work = Screen.FromPoint(point).WorkingArea;
            int commands = 0;
            foreach (ToolStripItem item in Items) if (!(item is ToolStripSeparator)) commands++;
            double designHeight = 10 + 39 * commands + 8 * (Items.Count - commands);
            double dpiScale = Native.MonitorDpi(point) / 96.0;
            float scale = (float)Math.Min(dpiScale, Math.Min((work.Height - 12) / designHeight, (work.Width - 12) / 310.0));
            ApplyScale(Math.Max(.55f, scale));
        }

        protected override void OnOpened(EventArgs e)
        {
            Rectangle work = Screen.FromPoint(anchor).WorkingArea;
            Rectangle near = Bounds;
            near.Inflate(3,3);
            if (!work.Contains(Bounds) || !near.Contains(anchor))
            {
                int x = anchor.X + Width <= work.Right ? anchor.X : anchor.X - Width;
                int y = anchor.Y + Height <= work.Bottom ? anchor.Y : anchor.Y - Height;
                Location = new Point(Math.Max(work.Left, Math.Min(work.Right-Width, x)),
                                     Math.Max(work.Top, Math.Min(work.Bottom-Height, y)));
            }
            base.OnOpened(e);
        }

        void ApplyScale(float scale)
        {
            if (Math.Abs(appliedScale - scale) < .001f) return;
            appliedScale = scale;
            Font old = labelFont;
            labelFont = new Font("Microsoft YaHei UI", 20f * scale, FontStyle.Regular, GraphicsUnit.Pixel);
            StyleList(this, scale);
            if (old != null) old.Dispose();
        }

        void StyleList(ToolStripDropDown list, float scale)
        {
            list.SuspendLayout();
            try
            {
                list.Font = labelFont;
                int width = (int)Math.Round(180 * scale);
                foreach (ToolStripItem item in list.Items)
                {
                    int textWidth = TextRenderer.MeasureText(item.Text, labelFont).Width;
                    width = Math.Max(width, textWidth + (int)Math.Ceiling(72 * scale));
                }
                foreach (ToolStripItem item in list.Items)
                {
                    item.Font = labelFont;
                    item.AutoSize = false;
                    bool separator = item is ToolStripSeparator;
                    item.Size = new Size(width, (int)Math.Round((separator ? 8 : 39) * scale));
                    var group = item as ToolStripDropDownItem;
                    if (group != null && group.HasDropDownItems) StyleList(group.DropDown, scale);
                }
            }
            finally { list.ResumeLayout(true); }
        }

        static bool ContainsPress(ToolStripDropDown list, Point screen)
        {
            if (!list.Visible) return false;
            if (list.Bounds.Contains(screen)) return true;
            foreach (ToolStripItem row in list.Items)
            {
                var group = row as ToolStripDropDownItem;
                if (group != null && group.HasDropDownItems && ContainsPress(group.DropDown, screen)) return true;
            }
            return false;
        }

        internal void DismissOutside(Point screen)
        {
            if (Visible && !ContainsPress(this, screen)) Close(ToolStripDropDownCloseReason.AppClicked);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && labelFont != null) { labelFont.Dispose(); labelFont = null; }
        }
    }
}
