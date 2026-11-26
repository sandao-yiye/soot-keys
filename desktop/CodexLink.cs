using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace SootKeys
{
    // The desktop picker writes the selected id in config.toml. Opening or
    // dismissing the pet is a separate, device-local preference.
    internal sealed class CodexLink : IDisposable
    {
        const string LocalId = "custom:soot-keys";
        readonly string configPath, statePath, databasePath;
        readonly MascotMask mask = new MascotMask();
        static readonly Regex Section = new Regex(@"^\s*\[([^\]]+)\]\s*(?:#.*)?$");
        static readonly Regex Selection = new Regex(
            @"^\s*(?:selected-avatar-id|""selected-avatar-id""|'selected-avatar-id')\s*=\s*([""'])([\w:-]+)\1\s*(?:#.*)?$");
        long configTime = -1, stateTime = -1;
        bool selected;
        bool sawDatabase;
        DateTime lastDatabaseRead;
        string selectedId;

        internal CodexLink(string home)
        {
            if (String.IsNullOrWhiteSpace(home)) home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrWhiteSpace(home)) home = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            configPath = Path.Combine(home, "config.toml");
            statePath = Path.Combine(home, ".codex-global-state.json");
            databasePath = Path.Combine(home, ".codex-global-state.sqlite");
        }

        internal Point? Anchor { get { return mask.Anchor; } }
        internal bool Refresh() { return Refresh(true); }
        internal bool Refresh(bool cover)
        {
            if (IsSelected() && mask.HostPresent()) return mask.Refresh(cover);
            mask.Release();
            return false;
        }

        internal bool IsSelected()
        {
            try
            {
                long stamp=File.GetLastWriteTimeUtc(configPath).Ticks;
                if (configTime!=stamp)
                {
                    string id=ReadSelection(ReadShared(configPath));
                    if (id!=selectedId)
                    {
                        selected=false;
                        stateTime=-1;
                    }
                    selectedId=id;
                    configTime=stamp;
                }
                if (selectedId!=LocalId && (selectedId==null ||
                    !selectedId.StartsWith("pet_",StringComparison.Ordinal))) return false;
                if (!sawDatabase)
                {
                    try { File.GetAttributes(databasePath); sawDatabase=true; }
                    catch (FileNotFoundException) { } // A legacy home may have only JSON.
                }
                if (sawDatabase)
                {
                    bool open;
                    string json;
                    var result=CodexStateDatabase.Read(databasePath,selectedId==LocalId,out open,out json);
                    if (result==CodexStateDatabase.Result.Busy)
                        return selected && DateTime.UtcNow-lastDatabaseRead < TimeSpan.FromSeconds(1);
                    if (result!=CodexStateDatabase.Result.Ready) return selected=false;
                    lastDatabaseRead=DateTime.UtcNow;
                    var accounts=json==null ? null : new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string,object>;
                    return selected=open && (selectedId==LocalId || IsMapped(accounts,selectedId));
                }
                // Pre-SQLite clients stored both settings and owner atoms in one JSON file.
                stamp=File.GetLastWriteTimeUtc(statePath).Ticks;
                if (stamp!=stateTime)
                {
                    selected=Inspect(ReadShared(statePath),selectedId);
                    stateTime=stamp;
                }
                return selected;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            configTime=-1;
            return selected=false;
        }

        static string ReadShared(string file)
        {
            using (FileStream stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            using (StreamReader reader = new StreamReader(stream)) return reader.ReadToEnd();
        }

        static string ReadSelection(string text)
        {
            bool desktop = false;
            using (StringReader reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    Match section = Section.Match(line);
                    if (section.Success || line.TrimStart().StartsWith("[", StringComparison.Ordinal))
                    {
                        desktop = section.Success && section.Groups[1].Value.Trim().Trim('"', '\'') == "desktop";
                        continue;
                    }
                    if (!desktop) continue;
                    Match match = Selection.Match(line);
                    if (match.Success) return match.Groups[2].Value;
                }
            }
            return null;
        }

        static object Value(Dictionary<string, object> d, string key)
        {
            object result;
            return d != null && d.TryGetValue(key, out result) ? result : null;
        }

        static bool Inspect(string json, string id)
        {
            var parser = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
            var root = parser.DeserializeObject(json) as Dictionary<string, object>;
            if (!Object.Equals(true, Value(root, "electron-avatar-overlay-open"))) return false;
            if (id == LocalId) return true;
            if (id == null || !id.StartsWith("pet_", StringComparison.Ordinal)) return false;
            string owner = Value(root, "electron-renderer-content-owner") as string;
            var atoms = Value(root, owner == null ? "electron-persisted-atom-state" :
                "electron-owner-atoms:" + owner) as Dictionary<string, object>;
            var accounts = Value(atoms, "migrated-cloud-pet-ids-v1") as Dictionary<string, object>;
            return IsMapped(accounts,id);
        }

        static bool IsMapped(Dictionary<string,object> accounts,string id)
        {
            if (accounts != null)
                foreach (var account in accounts.Values)
                    if (Object.Equals(id, Value(account as Dictionary<string, object>, LocalId))) return true;
            return false;
        }

        internal static void Verify()
        {
            const string state = "{\"electron-avatar-overlay-open\":true,\"electron-renderer-content-owner\":\"a\"," +
                "\"electron-owner-atoms:a\":{\"migrated-cloud-pet-ids-v1\":{\"current\":{\"custom:soot-keys\":\"pet_good\"}}}," +
                "\"electron-owner-atoms:b\":{\"migrated-cloud-pet-ids-v1\":{\"other\":{\"custom:soot-keys\":\"pet_wrong\"}}}}";
            string sample = "[other]\nselected-avatar-id='bad'\n[desktop] # picker\nselected-avatar-id = \"custom:soot-keys\" # cat\n";
            if (ReadSelection(sample) != LocalId || !Inspect(state, LocalId) || !Inspect(state, "pet_good") ||
                Inspect(state, "pet_wrong") || Inspect(state.Replace("true","false"), LocalId) || Inspect("{}",LocalId) ||
                ReadSelection("[desktop]\n[other]\nselected-avatar-id='custom:soot-keys'") != null)
                throw new InvalidOperationException("Codex picker state tests failed.");
            MascotMask.Verify();
        }

        public void Dispose() { mask.Dispose(); }
    }
}
