using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        readonly string configPath, statePath;
        readonly MascotMask mask = new MascotMask();
        static readonly Regex Section = new Regex(@"^\s*\[([^\]]+)\]\s*(?:#.*)?$");
        static readonly Regex Selection = new Regex(
            @"^\s*(?:selected-avatar-id|""selected-avatar-id""|'selected-avatar-id')\s*=\s*([""'])([\w:-]+)\1\s*(?:#.*)?$");
        long configTime = -1, stateTime = -1;
        bool selected;
        string selectedId;

        internal CodexLink(string home)
        {
            if (String.IsNullOrWhiteSpace(home)) home = Environment.GetEnvironmentVariable("CODEX_HOME");
            if (String.IsNullOrWhiteSpace(home)) home = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
            configPath = Path.Combine(home, "config.toml");
            statePath = Path.Combine(home, ".codex-global-state.json");
        }

        internal bool Refresh() { return Refresh(true); }
        internal bool Refresh(bool cover)
        {
            try
            {
                long configStamp = File.GetLastWriteTimeUtc(configPath).Ticks;
                long stateStamp = File.GetLastWriteTimeUtc(statePath).Ticks;
                if (configTime != configStamp || stateTime != stateStamp)
                {
                    if (configTime != configStamp) selectedId = ReadSelection(ReadShared(configPath));
                    selected = selectedId != null && Inspect(ReadShared(statePath), selectedId);
                    configTime = configStamp;
                    stateTime = stateStamp;
                }
                if (selected && mask.HostPresent())
                    return mask.Refresh(cover);
            }
            catch (IOException) { configTime = -1; }
            catch (UnauthorizedAccessException) { configTime = -1; }
            catch (ArgumentException) { configTime = -1; }
            catch (InvalidOperationException) { configTime = -1; }
            mask.Release();
            return false;
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
