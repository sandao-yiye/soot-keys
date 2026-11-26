using System;
using System.Diagnostics;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace SootKeys
{
    // All versions in this logon session share the v1.0 presence mutex.
    internal sealed class InstanceGate : IDisposable
    {
        const string Prefix = @"Local\SootKeys.LiveCat.";
        readonly Mutex presence = new Mutex(false, Prefix + "1");
        EventWaitHandle stop, manual;
        MemoryMappedFile identity;
        bool owns;
        internal bool Stopping() { return stop.WaitOne(0); }
        internal bool ManualRequested() { return manual.WaitOne(0); }

        static bool Notify(string name)
        {
            EventWaitHandle signal;
            if (!EventWaitHandle.TryOpenExisting(Prefix + name, out signal)) return false;
            using (signal) { signal.Set(); }
            return true;
        }

        static bool StopExisting(string kind)
        {
            EventWaitHandle signal;
            if (!EventWaitHandle.TryOpenExisting(Prefix + "Stop." + kind, out signal)) return false;
            using (signal)
            {
                MemoryMappedFile identity;
                try { identity = MemoryMappedFile.OpenExisting(Prefix + "Pid." + kind, MemoryMappedFileRights.Read); }
                catch (FileNotFoundException) { return false; }
                using (identity)
                using (MemoryMappedViewAccessor view = identity.CreateViewAccessor(0, 4, MemoryMappedFileAccess.Read))
                {
                    Process owner;
                    try { owner = Process.GetProcessById(view.ReadInt32(0)); }
                    catch (ArgumentException) { return true; }
                    using (owner)
                    {
                        signal.Set();
                        if (!owner.WaitForExit(10000)) throw new TimeoutException("墨爪仍在退出，请稍后重试。");
                    }
                }
            }
            return true;
        }

        internal static void StopLinked()
        {
            Mutex startup;
            if (!Mutex.TryOpenExisting(Prefix + "1", out startup)) return;
            using (startup)
            {
                // An installer may request shutdown immediately after starting the owner,
                // before its stop event and process id have been published.
                for (int i = 0; i < 20; i++)
                {
                    if (StopExisting("Linked")) return;
                    EventWaitHandle standalone;
                    if (EventWaitHandle.TryOpenExisting(Prefix + "Stop.Standalone", out standalone))
                    { standalone.Dispose(); return; }
                    try { if (!startup.WaitOne(50)) continue; }
                    catch (AbandonedMutexException) { }
                    startup.ReleaseMutex();
                    return;
                }
            }
        }

        bool Acquire(int wait)
        {
            try { owns = presence.WaitOne(wait); }
            catch (AbandonedMutexException) { owns = true; }
            return owns;
        }

        internal bool Start(bool linked)
        {
            if (!Acquire(0))
            {
                // Another launcher can be between acquiring the mutex and setting up IPC.
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    if (linked)
                    {
                        EventWaitHandle existing;
                        if (EventWaitHandle.TryOpenExisting(Prefix + "Stop.Linked", out existing))
                        { existing.Dispose(); return false; }
                        if (StopExisting("Standalone") || (attempt == 2 && CloseLegacy())) break;
                    }
                    else if (Notify("Manual")) return false;
                    if (Acquire(75)) break;
                }
                if (!owns && !Acquire(500))
                {
                    if (!linked) return false;
                    throw new InvalidOperationException("请退出旧版墨爪后重新启动联动。");
                }
            }
            string kind = linked ? "Linked" : "Standalone";
            identity = MemoryMappedFile.CreateOrOpen(Prefix + "Pid." + kind, 4);
            using (MemoryMappedViewAccessor view = identity.CreateViewAccessor())
            using (Process self = Process.GetCurrentProcess()) view.Write(0, self.Id);
            manual = new EventWaitHandle(false, EventResetMode.AutoReset, Prefix + "Manual");
            stop = new EventWaitHandle(false, EventResetMode.ManualReset, Prefix + "Stop." + kind);
            return true;
        }

        delegate bool WindowVisitor(IntPtr window, IntPtr state);
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor visit, IntPtr state);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window, StringBuilder buffer, int size);
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr window, uint msg, IntPtr w, IntPtr l);

        static bool CloseLegacy()
        {
            using (Process current = Process.GetCurrentProcess())
            foreach (Process old in Process.GetProcessesByName("SootKeys"))
            using (old)
            {
                if (old.Id == current.Id || old.SessionId != current.SessionId) continue;
                try
                {
                    FileVersionInfo info = FileVersionInfo.GetVersionInfo(old.MainModule.FileName);
                    if (info.ProductName != "Soot Keys" || info.FileVersion != "1.0.0.0") continue;
                    bool sent = false;
                    EnumWindows(delegate(IntPtr hwnd, IntPtr unused)
                    {
                        uint pid;
                        GetWindowThreadProcessId(hwnd, out pid);
                        if (pid != old.Id) return true;
                        StringBuilder title = new StringBuilder(64);
                        GetWindowText(hwnd, title, title.Capacity);
                        if (title.ToString() != "Soot Keys · 墨爪") return true;
                        sent = PostMessage(hwnd, 0x10, IntPtr.Zero, IntPtr.Zero);
                        return !sent;
                    }, IntPtr.Zero);
                    if (sent && old.WaitForExit(5000)) return true;
                }
                catch (System.ComponentModel.Win32Exception) { }
                catch (InvalidOperationException) { }
            }
            return false;
        }

        public void Dispose()
        {
            if (stop != null) stop.Dispose();
            if (manual != null) manual.Dispose();
            if (identity != null) identity.Dispose();
            if (owns) presence.ReleaseMutex();
            presence.Dispose();
        }
    }
}
