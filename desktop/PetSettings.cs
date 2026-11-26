using System;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace SootKeys
{
    internal sealed class PetSettings
    {
        internal static readonly string[] Sizes = { "小巧", "标准", "大", "超大", "巨型" };
        // Relative to the reference pet's standard 662 px stage.
        static readonly int[] StageWidths = { 560, 662, 760, 860, 960 };
        internal const int Standard = 1;
        internal int Level = Standard;
        internal bool Follow, Paused;
        internal bool ShowKeyName = true;
        internal Point? Anchor;
        readonly string file;
        internal string TestDirectory { get { return file == null ? null : Path.GetDirectoryName(file); } }

        internal static string DefaultPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                      "SootKeys", "settings.ini"); }
        }

        internal PetSettings(string path) { file = path; Reload(); }

        internal Size Pixels(uint dpi)
        {
            double scale = StageWidths[Level] * (double)dpi / (662 * 96);
            return new Size((int)Math.Round(192 * scale), (int)Math.Round(208 * scale));
        }

        internal void Reload()
        {
            int level = Standard, x = 0, y = 0;
            bool follow = false, paused = false, showKeyName = true, hasX = false, hasY = false;
            if (file != null && File.Exists(file))
            {
                try
                {
                    foreach (string line in File.ReadAllLines(file))
                    {
                        string[] parts = line.Split(new[]{'='}, 2);
                        if (parts.Length != 2) continue;
                        int parsed;
                        switch (parts[0])
                        {
                            case "size":
                                if (int.TryParse(parts[1], out parsed) && parsed >= 0 && parsed < Sizes.Length) level = parsed;
                                break;
                            case "follow": bool.TryParse(parts[1], out follow); break;
                            case "paused": bool.TryParse(parts[1], out paused); break;
                            case "showKeyName":
                                bool enabled;
                                if (bool.TryParse(parts[1], out enabled)) showKeyName = enabled;
                                break;
                            case "anchorX": hasX = int.TryParse(parts[1], out x); break;
                            case "anchorY": hasY = int.TryParse(parts[1], out y); break;
                        }
                    }
                }
                catch (IOException) { return; }
                catch (UnauthorizedAccessException) { return; }
            }
            Level = level; Follow = follow; Paused = paused; ShowKeyName = showKeyName;
            Anchor = hasX && hasY ? (Point?)new Point(x, y) : null;
        }

        internal bool Save()
        {
            if (file == null) return true;
            string dir = Path.GetDirectoryName(file);
            string temp = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Directory.CreateDirectory(dir);
                using (StreamWriter writer = File.CreateText(temp))
                {
                    writer.WriteLine("size=" + Level);
                    writer.WriteLine("follow=" + Follow);
                    writer.WriteLine("paused=" + Paused);
                    writer.WriteLine("showKeyName=" + ShowKeyName);
                    if (Anchor.HasValue)
                    {
                        writer.WriteLine("anchorX=" + Anchor.Value.X.ToString(CultureInfo.InvariantCulture));
                        writer.WriteLine("anchorY=" + Anchor.Value.Y.ToString(CultureInfo.InvariantCulture));
                    }
                }
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
