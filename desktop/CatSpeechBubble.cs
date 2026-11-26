using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SootKeys
{
    internal sealed class CatSpeechBubble : Control
    {
        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect { public int Left,Top,Right,Bottom; }
        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr window,out NativeRect rect);
        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr window,int command);
        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")]
        static extern bool SetLayeredWindowAttributes(IntPtr window,uint colorKey,byte alpha,uint flags);
        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr window);

        readonly Timer timer = new Timer { Interval = 75 };
        readonly Stopwatch shown = new Stopwatch();
        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        readonly Color cream=Color.FromArgb(255,249,228), ink=Color.FromArgb(54,45,38);
        Form pet;
        Font lettering;
        string line;
        double duration;
        int dpi, tail, padding, tip;
        bool above;
        GraphicsPath outline;

        internal CatSpeechBubble()
        {
            Text="墨爪的碎碎念";
            SetTopLevel(true);
            DoubleBuffered=true;
            BackColor=cream;
            // A layered window with WS_EX_TRANSPARENT forwards input to the
            // window underneath, including across process boundaries.
            timer.Tick+=delegate { UpdateBubble(shown.Elapsed.TotalMilliseconds); };
        }

        internal bool IsShowing { get { return IsHandleCreated && IsWindowVisible(Handle); } }
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp=base.CreateParams;
                cp.Style=unchecked((int)0x80000000); // WS_POPUP; initially hidden
                cp.ExStyle |= 0x08000000 | 0x00080000 | 0x00000080 | 0x00000020;
                return cp;
            }
        }

        internal static Rectangle PetBounds(Form pet)
        {
            NativeRect rect;
            if (!pet.IsDisposed && pet.IsHandleCreated && GetWindowRect(pet.Handle,out rect))
                return Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom);
            return pet.Bounds;
        }

        internal static int PetDpi(Form pet)
        {
            if (pet.IsHandleCreated)
                try { return Math.Max(96,(int)Native.GetDpiForWindow(pet.Handle)); }
                catch (EntryPointNotFoundException) { }
            return 96;
        }

        internal void Say(Form ownerPet,string text)
        {
            pet=ownerPet;
            line=text;
            AccessibleName=text;
            duration=Math.Max(4500,Math.Min(7500,2400+115*text.Length));
            dpi=0;
            shown.Restart();
            UpdateBubble(0);
            if (pet==null) return;
            SetLayeredWindowAttributes(Handle,0,254,2);
            SetWindowPos(Handle,new IntPtr(-1),0,0,0,0,0x53); // topmost, show, keep bounds and focus
            timer.Start();
        }

        internal void UpdateBubble(double elapsedMilliseconds)
        {
            if (pet==null || pet.IsDisposed || !pet.Visible || elapsedMilliseconds>=duration)
            {
                timer.Stop();
                shown.Reset();
                if (IsHandleCreated) ShowWindow(Handle,0);
                pet=null;
                return;
            }
            Rectangle cat=PetBounds(pet);
            Rectangle desk=Screen.FromRectangle(cat).WorkingArea;
            int newDpi=PetDpi(pet);
            if (newDpi!=dpi)
            {
                dpi=newDpi;
                padding=Px(14);
                tail=Px(9);
                if (lettering!=null) lettering.Dispose();
                lettering=new Font("Microsoft YaHei UI",Px(17),FontStyle.Regular,GraphicsUnit.Pixel);
                int maxText=Math.Max(1,Math.Min(Px(270),desk.Width-2*Px(8)-2*padding));
                Size content=TextRenderer.MeasureText(line,lettering,new Size(maxText,Int32.MaxValue),Flags);
                ClientSize=new Size(Math.Min(maxText,content.Width)+2*padding,content.Height+2*Px(10)+tail);
                if (outline!=null) { outline.Dispose(); outline=null; }
            }
            bool placeAbove;
            int newTip;
            Point position=Place(cat,desk,Size,Px(7),Px(24),out placeAbove,out newTip);
            if (Location!=position) Location=position;
            if (outline==null || placeAbove!=above || newTip!=tip)
            {
                above=placeAbove; tip=newTip;
                if (outline!=null) outline.Dispose();
                outline=MakeOutline();
                Region old=Region;
                Region=new Region(outline);
                if (old!=null) old.Dispose();
                Invalidate();
            }
        }

        internal static Point Place(Rectangle cat,Rectangle desk,Size size,int gap,int inset,out bool above,out int tip)
        {
            int center=cat.Left+cat.Width/2;
            int x=Clamp(center-size.Width/2,desk.Left+gap,desk.Right-gap-size.Width);
            int topRoom=cat.Top-desk.Top, bottomRoom=desk.Bottom-cat.Bottom;
            above=topRoom>=size.Height+2*gap || topRoom>=bottomRoom;
            int y=above ? cat.Top-gap-size.Height : cat.Bottom+gap;
            y=Clamp(y,desk.Top+gap,desk.Bottom-gap-size.Height);
            tip=Clamp(center-x,inset,size.Width-inset);
            return new Point(x,y);
        }

        static int Clamp(int n,int min,int max) { return Math.Max(min,Math.Min(max,n)); }
        int Px(int value) { return (int)Math.Round(value*dpi/96.0); }

        GraphicsPath MakeOutline()
        {
            var path=new GraphicsPath();
            int top=above ? 1 : tail+1, bottom=Height-(above ? tail : 0)-1;
            int left=1, right=Width-1, radius=Px(12), arc=radius*2, wing=Px(7);
            path.AddArc(left,top,arc,arc,180,90);
            if (!above) {
                path.AddLine(tip-wing,top,tip,1);
                path.AddLine(tip,1,tip+wing,top);
            }
            path.AddArc(right-arc,top,arc,arc,270,90);
            path.AddArc(right-arc,bottom-arc,arc,arc,0,90);
            if (above) {
                path.AddLine(tip+wing,bottom,tip,Height-1);
                path.AddLine(tip,Height-1,tip-wing,bottom);
            }
            path.AddArc(left,bottom-arc,arc,arc,90,90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (outline==null) return;
            e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using (var edge=new Pen(Color.FromArgb(132,111,76),Math.Max(1,Px(1))))
                e.Graphics.DrawPath(edge,outline);
            int gap=Px(10);
            Rectangle text=new Rectangle(padding,(above ? 0 : tail)+gap,Width-2*padding,Height-tail-2*gap);
            TextRenderer.DrawText(e.Graphics,line,lettering,text,ink,Flags|TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                timer.Dispose();
                if (lettering!=null) lettering.Dispose();
                if (outline!=null) outline.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
