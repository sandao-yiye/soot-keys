using System;
using System.Collections.Generic;

namespace SootKeys
{
    internal enum Pose { Idle, Left, Right, Space, Enter, Mouse, Combo }

    internal struct InputEdge
    {
        internal const int FirstMouse = 0x20001, PauseKey = 0xe11d;
        internal readonly IntPtr Device;
        internal readonly int KeyId;
        internal readonly Pose Pose;
        internal readonly bool Down;
        internal readonly string Label;

        internal InputEdge(IntPtr device, int keyId, Pose pose, bool down, string label)
        {
            Device = device;
            KeyId = keyId;
            Pose = pose;
            Down = down;
            Label = label;
        }
    }

    internal sealed class Motion
    {
        readonly Random random;
        double lastKey = -10, started = -10, lastCombo = -10;
        double nextBlink, winkEnd = -10;
        bool secondBlink;
        int streak;
        Pose current = Pose.Idle;
        Pose pending = Pose.Idle;
        // Raw Input identifies the physical device. Two keyboards can hold the
        // same key independently. Released keys leave this list immediately.
        readonly List<InputEdge> held = new List<InputEdge>();
        internal int ActionKeyId { get; private set; }
        internal InputEdge Held { get { return held.Count == 0 ? default(InputEdge) : held[held.Count - 1]; } }
        internal InputEdge HeldKey
        {
            get
            {
                for (int i=held.Count-1; i>=0; i--)
                    if (held[i].Pose != Pose.Mouse) return held[i];
                return default(InputEdge);
            }
        }
        internal int HeldMouseButtons
        {
            get
            {
                int buttons = 0;
                foreach (InputEdge edge in held)
                    if (edge.Pose == Pose.Mouse) buttons |= 1 << (edge.KeyId - InputEdge.FirstMouse);
                return buttons;
            }
        }
        internal float GazeX, GazeY;

        internal Motion(int seed)
        {
            random = new Random(seed);
            nextBlink = 2 + random.NextDouble() * 2;
        }

        bool ComboBeat(Pose pose, double now)
        {
            if (pose == Pose.Mouse) return false;
            streak = now - lastKey < .23 ? streak + 1 : 1;
            lastKey = now;
            if (streak >= 6 && now - lastCombo > 1.15)
            {
                streak = 0;
                lastCombo = now;
                return true;
            }
            return false;
        }

        internal void Strike(Pose pose, double now)
        {
            if (ComboBeat(pose, now)) pose = Pose.Combo;

            // Keep special moves readable; the most recent new strike follows immediately.
            if ((current == Pose.Combo && now - started < .41)
                || ((current == Pose.Space || current == Pose.Enter) && now - started < .13 && pose != Pose.Combo))
            {
                pending = pose;
                return;
            }
            Begin(pose, now);
        }

        static double Duration(Pose pose)
        {
            return pose == Pose.Combo ? .52 : pose == Pose.Space || pose == Pose.Enter ? .33 : .22;
        }

        void Begin(Pose pose, double now, int keyId = 0)
        {
            current = pose;
            ActionKeyId = keyId;
            started = now;
            pending = Pose.Idle;
            if (pose == Pose.Enter) winkEnd = now + .19;
        }

        internal bool Apply(InputEdge edge, double now)
        {
            if (edge.KeyId == 0 || edge.Pose == Pose.Idle) return false;
            int index = held.Count-1;
            while (index >= 0 && (held[index].KeyId != edge.KeyId || held[index].Device != edge.Device)) index--;
            if (edge.Down)
            {
                if (index >= 0) return false; // Windows keyboard auto-repeat.
                // Pause/Break may have no corresponding break packet.
                if (edge.KeyId != InputEdge.PauseKey) held.Add(edge);
                bool combo = ComboBeat(edge.Pose, now);
                if (current == Pose.Combo && now - started < .52 && edge.Pose != Pose.Mouse)
                    ActionKeyId = edge.KeyId;
                else
                    Begin(combo ? Pose.Combo : edge.Pose, now, edge.KeyId);
                return true;
            }
            if (index < 0) return false;
            bool wasLatest = index == held.Count-1;
            held.RemoveAt(index);
            if (wasLatest && current != Pose.Combo && ActionKeyId == edge.KeyId)
            {
                double release = Duration(current) * .56;
                if (now - started >= release)
                {
                    if (held.Count != 0) ResumeHeld(now);
                    else started = now - release;
                }
            }
            return true;
        }

        void ResumeHeld(double now)
        {
            InputEdge edge = Held;
            // This paw has already struck once. Resume at contact without
            // counting a new keystroke or replaying the anticipation.
            Begin(edge.Pose, now - Duration(edge.Pose) * .4, edge.KeyId);
        }

        internal void RemoveDevice(IntPtr device, double now)
        {
            bool hadPriority = held.Count != 0 && Held.Device == device;
            held.RemoveAll(delegate(InputEdge e) { return e.Device == device; });
            if (hadPriority) ResumeHeld(now);
        }

        internal void ClearLabels()
        {
            for (int i=0; i<held.Count; i++)
            {
                InputEdge e = held[i];
                held[i] = new InputEdge(e.Device, e.KeyId, e.Pose, e.Down, null);
            }
        }

        internal void Audition(Pose pose, double now)
        {
            streak = 0;
            Begin(pose, now);
        }

        internal void Reset(double now)
        {
            current = pending = Pose.Idle;
            held.Clear();
            ActionKeyId = 0;
            streak = 0;
            lastKey = winkEnd = -10;
            nextBlink = now + 2 + random.NextDouble() * 2;
            secondBlink = false;
        }

        internal Pose Frame(double now, out int frame)
        {
            double elapsed = now - started;
            double duration = Duration(current);
            if (current != Pose.Combo && ActionKeyId != 0 && Held.KeyId == ActionKeyId)
                elapsed = Math.Min(elapsed, duration * .4);
            if (current != Pose.Idle && elapsed >= duration)
            {
                if (pending == Pose.Idle && held.Count != 0) ResumeHeld(now);
                else Begin(pending, now);
                elapsed = now - started;
                duration = Duration(current);
            }
            if (current == Pose.Idle)
            {
                frame = (int)(now * 4) % 6;
                return current;
            }
            // Start in an anticipation pose on the very first paint; spend two frames on contact.
            double t = elapsed / duration;
            frame = t < .11 ? 1 : t < .32 ? 2 : t < .56 ? 3 : t < .81 ? 4 : 5;
            return current;
        }

        internal float Blink(double now)
        {
            double elapsed = now - nextBlink;
            if (elapsed < 0) return 1;
            if (elapsed < .07) return (float)(1 - elapsed / .07);
            if (elapsed < .105) return 0;
            if (elapsed < .215) return (float)((elapsed - .105) / .11);
            secondBlink = !secondBlink && random.Next(6) == 0;
            nextBlink = now + (secondBlink ? .14 : 2.5 + random.NextDouble() * 3.5);
            return 1;
        }

        internal float RightLid(double now, float blink)
        {
            return now < winkEnd && now > winkEnd - .14 ? Math.Min(blink, .14f) : blink;
        }

        internal void Look(double x, double y, double seconds)
        {
            double magnitude = Math.Sqrt(x * x + y * y);
            double divisor = Math.Max(60, magnitude);
            float blend = (float)(1 - Math.Exp(-Math.Min(.1, seconds) * 18));
            GazeX += ((float)(x / divisor) - GazeX) * blend;
            GazeY += ((float)(y / divisor) - GazeY) * blend;
        }

        internal static void VerifyInput()
        {
            var cat = new Motion(1);
            int frame;
            IntPtr a = new IntPtr(10), b = new IntPtr(11);
            var space = new InputEdge(a, 0x39, Pose.Space, true, "空格");
            var rightMouse = new InputEdge(b, InputEdge.FirstMouse+1, Pose.Mouse, true, null);
            cat.Apply(space, 0);
            for (int i=1; i<20; i++)
                if (cat.Apply(space, i*.05)) throw new InvalidOperationException("Auto-repeat started another strike.");
            if (cat.Frame(12, out frame) != Pose.Space || frame != 3 || cat.ActionKeyId != space.KeyId)
                throw new InvalidOperationException("Sustained key failed to hold contact.");
            cat.Apply(rightMouse, 12);
            if (cat.Frame(24, out frame) != Pose.Mouse || frame != 3 || cat.HeldMouseButtons != 2 || cat.HeldKey.KeyId != space.KeyId)
                throw new InvalidOperationException("Sustained mouse / keyboard combination lost contact.");
            cat.Apply(new InputEdge(b, rightMouse.KeyId, Pose.Mouse, false, null), 25);
            if (cat.Frame(25, out frame) != Pose.Space || frame != 3 || cat.HeldMouseButtons != 0)
                throw new InvalidOperationException("Mouse release did not restore held key.");
            cat.Apply(new InputEdge(b, space.KeyId, Pose.Space, true, "空格"), 26);
            cat.Apply(new InputEdge(a, space.KeyId, Pose.Space, false, null), 26.2);
            if (cat.Held.Device != b || cat.Frame(35, out frame) != Pose.Space || frame != 3)
                throw new InvalidOperationException("One keyboard released the other keyboard.");
            cat.RemoveDevice(b, 35);
            if (cat.Frame(36, out frame) != Pose.Idle || cat.Held.KeyId != 0)
                throw new InvalidOperationException("Device removal left a held key.");
            cat.Apply(space, 37);
            cat.Apply(new InputEdge(a, space.KeyId, Pose.Space, false, null), 37.01);
            if (cat.Frame(37.14, out frame) != Pose.Space || frame != 3 || cat.Frame(38, out frame) != Pose.Idle)
                throw new InvalidOperationException("Short tap missed its contact or idle frame.");
            cat.Apply(new InputEdge(a, InputEdge.PauseKey, Pose.Right, true, "Pause"), 39);
            if (cat.Held.KeyId != 0 || cat.Frame(40, out frame) != Pose.Idle)
                throw new InvalidOperationException("Break-less Pause key remained held.");
        }
    }
}
