using System;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SootKeys
{
    internal sealed class LiveHost : ApplicationContext
    {
        readonly Timer timer = new Timer { Interval = 300 };
        readonly Func<bool, bool> selected;
        readonly Func<bool> stopping, showManual;
        readonly PetSettings preferences;
        readonly bool quiet;
        Illustration art;
        PetWindow cat;
        bool manual, shelved;
        volatile bool sessionLocked;
        bool exit;

        internal LiveHost(PetSettings settings, Func<bool, bool> selected, Func<bool> stopping,
                          Func<bool> showManual, bool quiet = false)
        {
            preferences = settings;
            this.selected = selected;
            this.stopping = stopping;
            this.showManual = showManual;
            this.quiet = quiet;
            timer.Tick += delegate { Refresh(); };
            if (!quiet) SystemEvents.SessionSwitch += OnSessionSwitch;
            timer.Start();
        }

        void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason == SessionSwitchReason.SessionLock || e.Reason == SessionSwitchReason.ConsoleDisconnect
                    || e.Reason == SessionSwitchReason.RemoteDisconnect) sessionLocked = true;
            else if (e.Reason == SessionSwitchReason.SessionUnlock || e.Reason == SessionSwitchReason.ConsoleConnect
                    || e.Reason == SessionSwitchReason.RemoteConnect) sessionLocked = false;
        }

        void Refresh()
        {
            if (exit) return;
            if (stopping()) { ExitThread(); return; }
            if (showManual()) { manual = true; shelved = false; }
            bool active = selected == null || selected(!shelved && !sessionLocked);
            if (!active) shelved = false;
            // Explicit portable launch opens the cat. A later Codex activation resumes host-linked mode.
            if (active) manual = false;
            if ((active || manual) && !shelved && !sessionLocked)
            {
                if (cat != null) return;
                if (art == null) art = new Illustration();
                cat = new PetWindow(art, preferences, false, quiet, selected != null);
                cat.FormClosed += UserClosed;
                cat.Show();
            }
            else DropWindow();
        }

        void UserClosed(object sender, FormClosedEventArgs e)
        {
            if (selected == null || e.CloseReason == CloseReason.WindowsShutDown) { ExitThread(); return; }
            cat.FormClosed -= UserClosed;
            cat = null;
            manual = false;
            shelved = true;
            selected(false);
        }

        void DropWindow()
        {
            if (cat == null) return;
            cat.FormClosed -= UserClosed;
            cat.Dispose();
            cat = null;
        }

        protected override void ExitThreadCore()
        {
            exit = true;
            timer.Stop();
            base.ExitThreadCore();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!quiet) SystemEvents.SessionSwitch -= OnSessionSwitch;
                timer.Dispose();
                DropWindow();
                if (art != null) art.Dispose();
            }
            base.Dispose(disposing);
        }

        internal static void Verify(PetSettings testSettings)
        {
            bool active = false, demand = false;
            bool covered = false;
            using (LiveHost host = new LiveHost(testSettings, delegate(bool mask) { covered = mask && active; return active; },
                      delegate { return false; }, delegate { bool requested = demand; demand = false; return requested; }, true))
            {
                host.Refresh();
                if (host.art != null || host.cat != null) throw new InvalidOperationException("Inactive bridge created a cat.");
                active = true;
                host.Refresh();
                PetWindow first = host.cat;
                if (first == null || !first.Visible) throw new InvalidOperationException("Selection did not open the cat.");
                host.Refresh();
                if (host.cat != first) throw new InvalidOperationException("Duplicate cat on repeated selection.");
                first.Close();
                host.Refresh();
                if (host.exit || host.cat != null || covered) throw new InvalidOperationException("Shelving must restore the native pet and keep listening.");
                active = false; host.Refresh();
                active = true; host.Refresh();
                first = host.cat;
                if (first == null || !covered) throw new InvalidOperationException("Reactivation after shelving failed.");
                host.sessionLocked = true; host.Refresh();
                if (host.cat != null || covered) throw new InvalidOperationException("Session lock left an active pet.");
                host.sessionLocked = false; host.Refresh();
                first = host.cat;
                if (first == null || !covered) throw new InvalidOperationException("Session unlock did not restore pet.");
                active = false;
                host.Refresh();
                if (host.cat != null || !first.IsDisposed) throw new InvalidOperationException("Deselection left cat alive.");
                demand = true;
                host.Refresh();
                if (host.cat == null) throw new InvalidOperationException("Standalone request failed.");
                active = true; host.Refresh();
                active = false; host.Refresh();
                if (host.cat != null) throw new InvalidOperationException("Standalone did not return to linked mode.");
            }
        }
    }
}
