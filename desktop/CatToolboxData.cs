using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace SootKeys
{
    // All data belongs to this cat. Focus time is checkpointed as remaining
    // seconds, so an app restart restores a paused session.
    internal sealed class CatToolboxData
    {
        readonly string path;
        internal readonly List<int> Favorites = new List<int>();
        internal readonly List<int> Recent = new List<int>();
        internal readonly List<int> RecentFortunes = new List<int>();
        internal readonly List<int> KeptFortunes = new List<int>();
        internal int Duration, Remaining;
        internal int CompletedRounds, CompletedMinutes, Best;
        internal string Day;
        internal bool CouldSave = true;

        internal CatToolboxData(string directory)
        {
            path = Path.Combine(directory, "toolbox.xml");
            try
            {
                if (File.Exists(path))
                {
                    XmlDocument doc = new XmlDocument();
                    doc.XmlResolver = null;
                    using (XmlReader reader = XmlReader.Create(path, new XmlReaderSettings {
                        DtdProcessing = DtdProcessing.Prohibit, MaxCharactersInDocument = 32768 }))
                        doc.Load(reader);
                    XmlElement root = doc.DocumentElement;
                    ReadList(root, "favorites", Favorites, 7);
                    ReadList(root, "recent", Recent, 7);
                    ReadList(root, "fortuneHistory", RecentFortunes, 16);
                    ReadList(root, "savedFortunes", KeptFortunes, 16);
                    Duration = ReadNumber(root, "duration", 0, 120);
                    Remaining = ReadNumber(root, "remaining", 0, Duration * 60);
                    if (Duration < 5 || Remaining == 0) Duration = Remaining = 0;
                    Day = root.GetAttribute("day");
                    CompletedRounds = ReadNumber(root, "rounds", 0, int.MaxValue);
                    CompletedMinutes = ReadNumber(root, "minutes", 0, int.MaxValue);
                    Best = ReadNumber(root, "best", 0, int.MaxValue);
                }
            }
            catch (XmlException) { }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            Today();
        }

        static int ReadNumber(XmlElement root, string name, int min, int max)
        {
            int v;
            return int.TryParse(root.GetAttribute(name), out v) && v >= min && v <= max ? v : 0;
        }

        static void ReadList(XmlElement root, string key, List<int> dest, int limit)
        {
            foreach (string item in root.GetAttribute(key).Split(','))
            {
                int value;
                if (int.TryParse(item, out value) && value >= 0 && value < limit && !dest.Contains(value))
                    dest.Add(value);
            }
        }

        internal void Today()
        {
            string today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (Day == today) return;
            Day = today;
            CompletedRounds = CompletedMinutes = Best = 0;
        }

        internal void StartFocus(int minutes)
        {
            if (minutes < 5 || minutes > 120) throw new ArgumentOutOfRangeException("minutes");
            Duration = minutes;
            Remaining = minutes * 60;
        }

        internal void FinishFocus()
        {
            if (Duration == 0) return;
            Today();
            CompletedRounds++;
            CompletedMinutes += Duration;
            Duration = Remaining = 0;
        }

        internal void Save()
        {
            Today();
            XmlDocument doc = new XmlDocument();
            XmlElement root = doc.CreateElement("toolbox");
            doc.AppendChild(root);
            root.SetAttribute("favorites", string.Join(",", Favorites));
            root.SetAttribute("recent", string.Join(",", Recent));
            root.SetAttribute("fortuneHistory", string.Join(",", RecentFortunes));
            root.SetAttribute("savedFortunes", string.Join(",", KeptFortunes));
            root.SetAttribute("duration", Duration.ToString(CultureInfo.InvariantCulture));
            root.SetAttribute("remaining", Remaining.ToString(CultureInfo.InvariantCulture));
            root.SetAttribute("day", Day);
            root.SetAttribute("rounds", CompletedRounds.ToString(CultureInfo.InvariantCulture));
            root.SetAttribute("minutes", CompletedMinutes.ToString(CultureInfo.InvariantCulture));
            root.SetAttribute("best", Best.ToString(CultureInfo.InvariantCulture));
            string temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                doc.Save(temp);
                if (File.Exists(path)) File.Replace(temp, path, null);
                else File.Move(temp, path);
                CouldSave = true;
            }
            catch (IOException) { CouldSave = false; }
            catch (UnauthorizedAccessException) { CouldSave = false; }
        }
    }
}
