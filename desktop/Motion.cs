using System;

namespace SootKeys
{
    internal enum Pose { Idle, Left, Right, Space, Enter, Mouse, Combo }

    internal sealed class Motion
    {
        readonly Random random;
        double lastKey = -10, started = -10, lastCombo = -10;
        double nextBlink, winkEnd = -10;
        bool secondBlink;
        int streak;
        Pose current = Pose.Idle;
        Pose pending = Pose.Idle;
        internal float GazeX, GazeY;

        internal Motion(int seed)
        {
            random = new Random(seed);
            nextBlink = 2 + random.NextDouble() * 2;
        }

        internal void Strike(Pose pose, double now)
        {
            if (pose != Pose.Mouse)
            {
                streak = now - lastKey < .23 ? streak + 1 : 1;
                lastKey = now;
                if (streak >= 6 && now - lastCombo > 1.15)
                {
                    pose = Pose.Combo;
                    streak = 0;
                    lastCombo = now;
                }
            }

            // Keep special moves readable; the most recent new strike follows immediately.
            if ((current == Pose.Combo && now - started < .41)
                || ((current == Pose.Space || current == Pose.Enter) && now - started < .13 && pose != Pose.Combo))
            {
                pending = pose;
                return;
            }
            Begin(pose, now);
        }

        void Begin(Pose pose, double now)
        {
            current = pose;
            started = now;
            pending = Pose.Idle;
            if (pose == Pose.Enter) winkEnd = now + .19;
        }

        internal void Reset(double now)
        {
            current = pending = Pose.Idle;
            streak = 0;
            lastKey = winkEnd = -10;
            nextBlink = now + 2 + random.NextDouble() * 2;
            secondBlink = false;
        }

        internal Pose Frame(double now, out int frame)
        {
            double elapsed = now - started;
            double duration = current == Pose.Combo ? .52 : current == Pose.Space || current == Pose.Enter ? .33 : .22;
            if (current != Pose.Idle && elapsed >= duration)
            {
                Begin(pending, now);
                elapsed = 0;
            }
            if (current == Pose.Idle)
            {
                frame = (int)(now * 4) % 6;
                return current;
            }
            // Start in an anticipation pose on the very first paint; spend two frames on contact.
            double t = elapsed / (current == Pose.Combo ? .52 : current == Pose.Space || current == Pose.Enter ? .33 : .22);
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
    }
}
