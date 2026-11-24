using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace TokeIME
{
    [Serializable]
    public sealed class AppSettings
    {
        public bool AutoLockEnabled { get; set; }
        public bool AutoAddPromptEnabled { get; set; }
        public bool BlockCtrlShift { get; set; }
        public bool BlockAltShift { get; set; }
        public bool BlockCtrlSpace { get; set; }
        public bool BlockWinSpace { get; set; }
        public bool BlockCapsLock { get; set; }
        public bool StartWithWindows { get; set; }
        public List<string> Processes { get; set; }
        public List<HotkeyDefinition> CustomHotkeys { get; set; }

        public AppSettings()
        {
            AutoLockEnabled = true;
            AutoAddPromptEnabled = false;
            BlockCtrlShift = true;
            BlockAltShift = true;
            BlockCtrlSpace = true;
            BlockWinSpace = true;
            BlockCapsLock = true;
            StartWithWindows = false;
            Processes = new List<string>();
            CustomHotkeys = new List<HotkeyDefinition>();
        }

        public static string ConfigDirectory
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TokeIME");
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDirectory, "config.json"); }
        }

        public static AppSettings Load()
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppSettings();
            }

            try
            {
                string json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                AppSettings settings = serializer.Deserialize<AppSettings>(json);
                if (settings == null)
                {
                    return new AppSettings();
                }

                settings.EnsureValid();
                return settings;
            }
            catch
            {
                // A malformed config should never prevent the tray app from starting.
                return new AppSettings();
            }
        }

        public void Save()
        {
            EnsureValid();
            Directory.CreateDirectory(ConfigDirectory);

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string json = serializer.Serialize(this);
            string temporaryPath = ConfigPath + ".tmp";

            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(ConfigPath))
            {
                File.Replace(temporaryPath, ConfigPath, null);
            }
            else
            {
                File.Move(temporaryPath, ConfigPath);
            }
        }

        public AppSettings Clone()
        {
            AppSettings copy = new AppSettings();
            copy.AutoLockEnabled = AutoLockEnabled;
            copy.AutoAddPromptEnabled = AutoAddPromptEnabled;
            copy.BlockCtrlShift = BlockCtrlShift;
            copy.BlockAltShift = BlockAltShift;
            copy.BlockCtrlSpace = BlockCtrlSpace;
            copy.BlockWinSpace = BlockWinSpace;
            copy.BlockCapsLock = BlockCapsLock;
            copy.StartWithWindows = StartWithWindows;
            copy.Processes = new List<string>(Processes);
            copy.CustomHotkeys = new List<HotkeyDefinition>();
            if (CustomHotkeys == null)
            {
                CustomHotkeys = new List<HotkeyDefinition>();
            }
            foreach (HotkeyDefinition hotkey in CustomHotkeys)
            {
                if (hotkey != null)
                {
                    copy.CustomHotkeys.Add(hotkey.Clone());
                }
            }
            return copy;
        }

        public bool IsConfiguredProcess(string processName)
        {
            if (string.IsNullOrEmpty(processName) || Processes == null)
            {
                return false;
            }

            string foregroundStem = GetProcessStem(processName);
            for (int index = 0; index < Processes.Count; index++)
            {
                if (string.Equals(Processes[index], processName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                string configuredStem = GetProcessStem(Processes[index]);
                if (foregroundStem != null &&
                    configuredStem != null &&
                    string.Equals(foregroundStem, configuredStem, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetProcessStem(string processName)
        {
            string normalized = NormalizeProcessName(processName);
            if (normalized == null)
            {
                return null;
            }

            string value = Path.GetFileNameWithoutExtension(normalized);
            string[] suffixes = new string[]
            {
                "_launcher", "-launcher", ".launcher", " launcher", "launcher",
                "_bootstrap", "-bootstrap", "bootstrap", "_starter", "-starter", "starter"
            };
            foreach (string suffix in suffixes)
            {
                if (value.Length > suffix.Length &&
                    value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    value = value.Substring(0, value.Length - suffix.Length).Trim(' ', '_', '-', '.');
                    break;
                }
            }
            return value + ".exe";
        }

        public static string NormalizeProcessName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            string name;
            try
            {
                name = Path.GetFileName(value.Trim());
            }
            catch (ArgumentException)
            {
                return null;
            }
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name += ".exe";
            }

            return name;
        }

        private void EnsureValid()
        {
            if (Processes == null)
            {
                Processes = new List<string>();
            }

            List<string> normalized = new List<string>();
            for (int index = 0; index < Processes.Count; index++)
            {
                string name = NormalizeProcessName(Processes[index]);
                if (name == null)
                {
                    continue;
                }

                bool exists = false;
                for (int normalizedIndex = 0; normalizedIndex < normalized.Count; normalizedIndex++)
                {
                    if (string.Equals(normalized[normalizedIndex], name, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    normalized.Add(name);
                }
            }

            Processes = normalized;
            if (CustomHotkeys == null)
            {
                CustomHotkeys = new List<HotkeyDefinition>();
            }

            List<HotkeyDefinition> normalizedHotkeys = new List<HotkeyDefinition>();
            for (int index = 0; index < CustomHotkeys.Count; index++)
            {
                HotkeyDefinition hotkey = CustomHotkeys[index];
                if (hotkey == null || !hotkey.IsValid)
                {
                    continue;
                }

                bool duplicate = false;
                for (int normalizedIndex = 0; normalizedIndex < normalizedHotkeys.Count; normalizedIndex++)
                {
                    if (normalizedHotkeys[normalizedIndex].SameAs(hotkey))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    normalizedHotkeys.Add(hotkey.Clone());
                }
            }
            CustomHotkeys = normalizedHotkeys;
        }

    }
}
