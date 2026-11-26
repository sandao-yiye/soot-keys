using Accessibility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Automation;

namespace SootKeys
{
    // Only the named sprite inside the app's own /avatar-overlay document can
    // receive a cutout. The rest of the Electron window remains under Codex control.
    internal sealed class MascotMask : IDisposable
    {
        const string Owner = "SootKeys.MaskOwner.1", Shape = "SootKeys.MaskShape.1";
        static readonly int ThisPid = Process.GetCurrentProcess().Id;
        IntPtr masked, known;
        Rectangle lastFrame, lastSprite;
        Task<Hit> scan;
        DateTime nextScan, nextHostCheck;
        bool hostPresent;
        internal Point? Anchor { get; private set; }
        Process watchdog;
        EventWaitHandle watcherStop;
        readonly HashSet<int> processes = new HashSet<int>();

        sealed class Hit { internal IntPtr Window; internal Rectangle Frame, Sprite; }
        delegate bool EnumProc(IntPtr handle, IntPtr unused);
        [StructLayout(LayoutKind.Sequential)] struct Rect { internal int L,T,R,B; }
        [DllImport("user32")] static extern bool EnumWindows(EnumProc cb, IntPtr arg);
        [DllImport("user32")] static extern bool EnumChildWindows(IntPtr wnd, EnumProc cb, IntPtr arg);
        [DllImport("user32")] static extern bool IsWindowVisible(IntPtr wnd);
        [DllImport("user32")] static extern bool IsWindow(IntPtr wnd);
        [DllImport("user32")] static extern uint GetWindowThreadProcessId(IntPtr wnd, out int pid);
        [DllImport("user32",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr wnd,StringBuilder name,int length);
        [DllImport("user32")] static extern int GetWindowLong(IntPtr wnd,int index);
        [DllImport("user32")] static extern bool GetWindowRect(IntPtr wnd, out Rect rect);
        [DllImport("user32")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32")] static extern int GetWindowRgn(IntPtr wnd,IntPtr region);
        [DllImport("user32")] static extern int SetWindowRgn(IntPtr wnd,IntPtr region,bool redraw);
        [DllImport("user32",CharSet=CharSet.Unicode)] static extern bool SetProp(IntPtr wnd,string name,IntPtr value);
        [DllImport("user32",CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr wnd,string name);
        [DllImport("user32",CharSet=CharSet.Unicode)] static extern IntPtr RemoveProp(IntPtr wnd,string name);
        [DllImport("gdi32")] static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
        [DllImport("gdi32")] static extern int CombineRgn(IntPtr output,IntPtr a,IntPtr b,int mode);
        [DllImport("gdi32")] static extern bool DeleteObject(IntPtr region);
        [DllImport("gdi32")] static extern uint GetRegionData(IntPtr region,uint length,[Out] byte[] data);
        [DllImport("oleacc")] static extern int AccessibleObjectFromWindow(IntPtr wnd,uint obj, ref Guid iid,
            [MarshalAs(UnmanagedType.Interface)] out IAccessible acc);
        [DllImport("oleacc")] static extern int AccessibleChildren(IAccessible parent,int first,int count,
            [Out,MarshalAs(UnmanagedType.LPArray, SizeParamIndex=2)] object[] children,out int got);

        static string ClassName(IntPtr wnd)
        {
            StringBuilder name = new StringBuilder(128); GetClassName(wnd,name,name.Capacity); return name.ToString();
        }

        static Rectangle Bounds(IntPtr wnd)
        {
            Rect box;
            return GetWindowRect(wnd,out box) ? Rectangle.FromLTRB(box.L,box.T,box.R,box.B) : Rectangle.Empty;
        }

        internal bool HostPresent()
        {
            if (DateTime.UtcNow < nextHostCheck) return hostPresent;
            nextHostCheck = DateTime.UtcNow.AddSeconds(1);
            processes.Clear();
            foreach (string name in new[] { "ChatGPT","Codex" })
                foreach (Process process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        try
                        {
                            string file = process.MainModule.FileName;
                            string folder = Path.GetDirectoryName(file);
                            DirectoryInfo package = Directory.GetParent(folder);
                            if (Path.GetFileName(folder).Equals("app",StringComparison.OrdinalIgnoreCase) &&
                                package != null && package.Name.StartsWith("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase) &&
                                package.Name.EndsWith("__2p2nqsd0c76g0",StringComparison.OrdinalIgnoreCase))
                                processes.Add(process.Id);
                        }
                        catch (Win32Exception) { }
                        catch (InvalidOperationException) { }
                    }
            return hostPresent = processes.Count > 0;
        }

        static bool Route(string value)
        {
            if (value == null || !value.StartsWith("app://-/",StringComparison.Ordinal)) return false;
            string page = value.Substring(7).Split('#')[0];
            string[] parts = page.Split(new char[]{'?'},2);
            if (parts[0] == "/avatar-overlay") return true;
            if (parts.Length < 2) return false;
            foreach (string field in parts[1].Split('&'))
                if (field == "initialRoute=%2Favatar-overlay" || field == "initialRoute=/avatar-overlay" ||
                    field == "initialRoute=%2favatar-overlay") return true;
            return false;
        }

        // Read only MSAA roles, the document URL, and the named sprite bounds.
        // The app uses a dedicated accessible label on this element.
        static Rectangle FindSprite(IAccessible node, bool inOverlay, int depth, ref int budget, ref bool overlay, ref bool cold)
        {
            if (depth > 14 || --budget < 0) return Rectangle.Empty;
            int role = Convert.ToInt32(node.get_accRole(0));
            if (role == 15)
            {
                string url = node.get_accValue(0);
                if (string.IsNullOrEmpty(url) && node.accChildCount == 0) cold = true;
                inOverlay = Route(url);
                if (!inOverlay) return Rectangle.Empty;
                overlay = true;
            }
            if (inOverlay)
            {
                string name = node.get_accName(0);
                if ((name == "墨爪 pet" || name == "墨爪 智能伙伴") &&
                    (Convert.ToInt32(node.get_accState(0)) & 0x18000) == 0)
                {
                    int x,y,w,h; node.accLocation(out x,out y,out w,out h,0);
                    return new Rectangle(x,y,w,h);
                }
            }
            int n = Math.Min(128,node.accChildCount);
            if (n <= 0) return Rectangle.Empty;
            var children = new object[n]; int got;
            if (AccessibleChildren(node,0,n,children,out got) < 0) return Rectangle.Empty;
            for (int i=0;i<got;i++)
            {
                IAccessible child = children[i] as IAccessible;
                if (child == null) continue;
                Rectangle result = FindSprite(child,inOverlay,depth+1,ref budget,ref overlay,ref cold);
                if (!result.IsEmpty) return result;
            }
            return Rectangle.Empty;
        }

        // Chromium can initially return an empty MSAA document until a UIA client
        // requests the document provider. Only query this candidate HWND; MSAA
        // must still match both the Codex route and the exact sprite label.
        static void RequestDocument(IntPtr hwnd)
        {
            try
            {
                AutomationElement surface = AutomationElement.FromHandle(hwnd);
                if (surface != null)
                    surface.FindFirst(TreeScope.Descendants, new PropertyCondition(
                        AutomationElement.ControlTypeProperty, ControlType.Document));
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
            catch (InvalidOperationException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }

        static Hit Discover(HashSet<int> hosts)
        {
            Hit match = null;
            EnumWindows(delegate(IntPtr hwnd,IntPtr z)
            {
                int pid; GetWindowThreadProcessId(hwnd,out pid);
                if (!hosts.Contains(pid) || !IsWindowVisible(hwnd) || (GetWindowLong(hwnd,-20)&8)==0 ||
                    ClassName(hwnd)!="Chrome_WidgetWin_1") return true;
                bool overlay = false; Rectangle sprite = Rectangle.Empty;
                var surfaces = new List<IntPtr>();
                EnumChildWindows(hwnd,delegate(IntPtr c,IntPtr z2)
                {
                    if (ClassName(c)=="Chrome_RenderWidgetHostHWND") surfaces.Add(c);
                    return true;
                },IntPtr.Zero);
                surfaces.Add(hwnd);
                for (int pass=0; pass<2; pass++)
                {
                    bool cold = false;
                    foreach (IntPtr surface in surfaces)
                    {
                        Guid iid = typeof(IAccessible).GUID; IAccessible root;
                        try
                        {
                            if (AccessibleObjectFromWindow(surface,0xfffffffc,ref iid,out root) < 0 || root==null) continue;
                            int budget=220;
                            sprite=FindSprite(root,false,0,ref budget,ref overlay,ref cold);
                            if (!sprite.IsEmpty) break;
                        }
                        catch (COMException) { }
                        catch (InvalidCastException) { }
                        catch (ArgumentException) { }
                        catch (NotImplementedException) { }
                    }
                    if (!cold || overlay || pass != 0) break;
                    RequestDocument(hwnd);
                }
                if (!overlay) return true;
                match = new Hit { Window=hwnd,Frame=Bounds(hwnd),Sprite=sprite };
                return false;
            },IntPtr.Zero);
            return match;
        }

        internal bool Refresh(bool cover)
        {
            if (known != IntPtr.Zero && (!IsWindow(known) || !IsHostWindow(known)))
            { known = IntPtr.Zero; Anchor = null; }
            if (!cover) Anchor = null;
            if (!cover || masked != IntPtr.Zero && (GetForegroundWindow()==masked || Bounds(masked)!=lastFrame ||
                !IsWindowVisible(masked))) RestoreCurrent();
            if (scan != null && scan.IsCompleted)
            {
                Hit hit = scan.Status==TaskStatus.RanToCompletion ? scan.Result : null;
                if (scan.IsFaulted) GC.KeepAlive(scan.Exception);
                scan=null;
                if (hit != null && IsHostWindow(hit.Window) && IsWindowVisible(hit.Window))
                {
                    known=hit.Window;
                    bool verified = Bounds(known)==hit.Frame && ValidSprite(hit.Frame,hit.Sprite);
                    Anchor = verified && cover ? (Point?)new Point(hit.Sprite.Left + hit.Sprite.Width/2,hit.Sprite.Bottom) : null;
                    if (cover && GetForegroundWindow()!=known && verified) Apply(hit);
                    else RestoreCurrent();
                }
                else { Anchor = null; RestoreCurrent(); }
            }
            if (scan==null && DateTime.UtcNow>=nextScan)
            {
                nextScan=DateTime.UtcNow.AddMilliseconds(500);
                var hosts=new HashSet<int>(processes);
                scan=Task.Run(()=>Discover(hosts));
            }
            return known==IntPtr.Zero || IsWindowVisible(known);
        }

        bool IsHostWindow(IntPtr hwnd)
        {
            int pid; GetWindowThreadProcessId(hwnd,out pid); return processes.Contains(pid);
        }

        static bool ValidSprite(Rectangle frame,Rectangle sprite)
        {
            // The Codex v2 asset is a 192 x 208 cell. Use physical screen
            // coordinates from MSAA and GetWindowRect in the same DPI context.
            double ratio=(double)sprite.Width/Math.Max(1,sprite.Height);
            return frame.Contains(sprite) && sprite.Width>=32 && sprite.Width<=896 &&
                ratio>0.85 && ratio<1.08;
        }

        static int Fingerprint(IntPtr region)
        {
            uint n=GetRegionData(region,0,null);
            if (n==0 || n>4096) return 0;
            byte[] data=new byte[n];
            if (GetRegionData(region,n,data)!=n) return 0;
            uint hash=2166136261;
            unchecked { foreach (byte b in data) hash=(hash ^ b)*16777619; }
            return (int)(hash&0x7fffffff) | 1;
        }

        void Apply(Hit hit)
        {
            if (masked==hit.Window && lastFrame==hit.Frame && lastSprite==hit.Sprite) return;
            RestoreCurrent();
            if (watchdog == null || watchdog.HasExited)
            {
                try
                {
                    var info = new ProcessStartInfo(Application.ExecutablePath,"--watch-mask "+ThisPid);
                    info.UseShellExecute=false; info.CreateNoWindow=true;
                    if (watchdog!=null) watchdog.Dispose();
                    if (watcherStop==null)
                        watcherStop=new EventWaitHandle(false,EventResetMode.ManualReset,WatchEvent(ThisPid));
                    watchdog=Process.Start(info);
                    if (watchdog==null) return;
                }
                catch(Win32Exception) { return; }
            }
            if (!Cut(hit.Window,hit.Frame,hit.Sprite)) return;
            masked=hit.Window; lastFrame=hit.Frame; lastSprite=hit.Sprite;
        }

        static bool Cut(IntPtr hwnd,Rectangle frame,Rectangle sprite)
        {
            IntPtr region=CreateRectRgn(0,0,frame.Width,frame.Height);
            IntPtr hole=CreateRectRgn(sprite.Left-frame.Left,sprite.Top-frame.Top,sprite.Right-frame.Left,sprite.Bottom-frame.Top);
            IntPtr existing=CreateRectRgn(0,0,0,0);
            try
            {
                if (region==IntPtr.Zero || hole==IntPtr.Zero || existing==IntPtr.Zero ||
                    GetProp(hwnd,Owner)!=IntPtr.Zero || GetWindowRgn(hwnd,existing)!=0 ||
                    CombineRgn(region,region,hole,4)==0) return false;
                int shape=Fingerprint(region);
                if (shape==0 || !SetProp(hwnd,Owner,new IntPtr(ThisPid))) return false;
                if (!SetProp(hwnd,Shape,new IntPtr(shape)) || SetWindowRgn(hwnd,region,true)==0)
                {
                    RemoveProp(hwnd,Shape); RemoveProp(hwnd,Owner); return false;
                }
                region=IntPtr.Zero; // ownership transferred to Windows
                return true;
            }
            finally { if (region!=IntPtr.Zero)DeleteObject(region); DeleteObject(hole); DeleteObject(existing); }
        }

        static void Restore(IntPtr wnd,int owner)
        {
            if (wnd==IntPtr.Zero || GetProp(wnd,Owner)!=new IntPtr(owner))return;
            IntPtr region=CreateRectRgn(0,0,0,0);
            try
            {
                if (GetWindowRgn(wnd,region)!=0 && new IntPtr(Fingerprint(region))==GetProp(wnd,Shape))
                    SetWindowRgn(wnd,IntPtr.Zero,true);
            }
            finally { DeleteObject(region); RemoveProp(wnd,Shape); RemoveProp(wnd,Owner); }
        }

        static string WatchEvent(int pid) { return @"Local\SootKeys.Mask.Stop." + pid; }

        internal static int Watch(string value)
        {
            int pid; if (!Int32.TryParse(value,out pid) || pid<=0 || pid==ThisPid) return 1;
            try
            {
                using (Process parent=Process.GetProcessById(pid))
                using (var stop=new EventWaitHandle(false,EventResetMode.ManualReset,WatchEvent(pid)))
                    while (!parent.WaitForExit(200) && !stop.WaitOne(0)) { }
            }
            catch(ArgumentException) { }
            EnumWindows(delegate(IntPtr hwnd,IntPtr z){ Restore(hwnd,pid); return true; },IntPtr.Zero);
            return 0;
        }
        void RestoreCurrent() { Restore(masked,ThisPid); masked=IntPtr.Zero; }
        internal void Release()
        {
            RestoreCurrent();
            Anchor = null;
            known = IntPtr.Zero;
        }
        public void Dispose()
        {
            Release();
            if (watcherStop!=null) watcherStop.Set();
            if (watchdog!=null) { watchdog.WaitForExit(2000); watchdog.Dispose(); }
            if (watcherStop!=null) watcherStop.Dispose();
        }

        internal static void Verify()
        {
            Rectangle frame = new Rectangle(100,160,480,590), sprite=new Rectangle(310,190,112,122);
            if (!Route("app://-/index.html?initialRoute=%2Favatar-overlay") ||
                Route("https://example.org/avatar-overlay") || Route("app://-/index.html?initialRoute=/settings") ||
                !ValidSprite(frame,sprite) || ValidSprite(frame,new Rectangle(90,190,112,122)))
                throw new InvalidOperationException("Native overlay identity/geometry checks failed.");
            using(Form fixture=new Form())
            {
                IntPtr window=fixture.Handle;
                Rectangle host=Bounds(window);
                if (!Cut(window,host,new Rectangle(host.Left+4,host.Top+4,30,30))) throw new InvalidOperationException("Fixture clip failed.");
                Restore(window,ThisPid);
                IntPtr test=CreateRectRgn(0,0,0,0);
                try { if (GetWindowRgn(window,test)!=0) throw new InvalidOperationException("Fixture clip not restored."); }
                finally { DeleteObject(test); }
            }
        }
    }
}
