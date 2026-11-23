using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace TokeIME
{
    internal enum SoftwareSearchScope
    {
        Global,
        Desktop,
        Running
    }

    internal sealed class SoftwareSearchForm : Form
    {
        private readonly TextBox _searchBox;
        private readonly Button _searchButton;
        private readonly Label _statusLabel;
        private readonly ListView _resultList;
        private readonly Label _pathLabel;
        private readonly Label _sourceLabel;
        private readonly ImageList _iconList;
        private readonly Button _selectButton;
        private readonly Timer _searchDebounceTimer;
        private readonly string _initialQuery;
        private readonly SoftwareSearchScope _scope;

        public string SelectedProcessName { get; private set; }
        public string SelectedExecutablePath { get; private set; }

        public SoftwareSearchForm(string initialQuery, SoftwareSearchScope scope)
        {
            _initialQuery = initialQuery == null ? string.Empty : initialQuery.Trim();
            _scope = scope;

            Text = _scope == SoftwareSearchScope.Desktop
                ? "Desktop Search"
                : (_scope == SoftwareSearchScope.Running ? "Add Running" : "Global Search");
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(780, 610);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            Label titleLabel = new Label();
            titleLabel.Text = _scope == SoftwareSearchScope.Desktop
                ? "Search Desktop Shortcuts and EXEs"
                : (_scope == SoftwareSearchScope.Running ? "Select a Running Program" : "Search for a Program to Auto-Lock");
            titleLabel.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.Location = new Point(18, 14);
            titleLabel.Size = new Size(500, 34);
            Controls.Add(titleLabel);

            _searchBox = new TextBox();
            _searchBox.Location = new Point(20, 56);
            _searchBox.Size = new Size(600, 27);
            _searchBox.Text = _initialQuery;
            _searchBox.KeyDown += delegate(object sender, KeyEventArgs eventArgs)
            {
                if (eventArgs.KeyCode == Keys.Enter)
                {
                    eventArgs.SuppressKeyPress = true;
                    Search();
                }
            };

            _searchDebounceTimer = new Timer();
            _searchDebounceTimer.Interval = 500;
            _searchDebounceTimer.Tick += delegate
            {
                _searchDebounceTimer.Stop();
                if (_scope != SoftwareSearchScope.Global || _searchBox.Text.Trim().Length > 0)
                {
                    Search();
                }
            };
            _searchBox.TextChanged += delegate
            {
                _searchDebounceTimer.Stop();
                _searchDebounceTimer.Start();
            };
            Controls.Add(_searchBox);

            _searchButton = new Button();
            _searchButton.Text = "Search";
            _searchButton.Location = new Point(630, 54);
            _searchButton.Size = new Size(130, 29);
            _searchButton.Click += delegate { Search(); };
            Controls.Add(_searchButton);

            _statusLabel = new Label();
            _statusLabel.Text = string.Empty;
            _statusLabel.ForeColor = SystemColors.ControlText;
            _statusLabel.Location = new Point(20, 92);
            _statusLabel.Size = new Size(740, 22);
            Controls.Add(_statusLabel);

            _resultList = new ListView();
            _resultList.Location = new Point(20, 120);
            _resultList.Size = new Size(740, 360);
            _resultList.View = View.LargeIcon;
            _resultList.LargeImageList = _iconList = new ImageList();
            _iconList.ImageSize = new Size(64, 64);
            _iconList.ColorDepth = ColorDepth.Depth32Bit;
            _resultList.HideSelection = false;
            _resultList.MultiSelect = false;
            _resultList.ShowItemToolTips = true;
            _resultList.SelectedIndexChanged += delegate { UpdateSelectionDetails(); };
            _resultList.DoubleClick += delegate { AcceptSelection(); };
            _resultList.KeyDown += delegate(object sender, KeyEventArgs eventArgs)
            {
                if (eventArgs.KeyCode == Keys.Enter)
                {
                    eventArgs.SuppressKeyPress = true;
                    AcceptSelection();
                }
            };
            Controls.Add(_resultList);

            Label pathTitle = new Label();
            pathTitle.Text = "Path";
            pathTitle.Location = new Point(20, 490);
            pathTitle.Size = new Size(54, 20);
            Controls.Add(pathTitle);

            _pathLabel = new Label();
            _pathLabel.Text = "Nothing selected";
            _pathLabel.AutoEllipsis = true;
            _pathLabel.ForeColor = SystemColors.ControlText;
            _pathLabel.Location = new Point(76, 490);
            _pathLabel.Size = new Size(684, 20);
            Controls.Add(_pathLabel);

            Label sourceTitle = new Label();
            sourceTitle.Text = "Source";
            sourceTitle.Location = new Point(20, 514);
            sourceTitle.Size = new Size(54, 20);
            Controls.Add(sourceTitle);

            _sourceLabel = new Label();
            _sourceLabel.Text = "—";
            _sourceLabel.ForeColor = SystemColors.ControlText;
            _sourceLabel.Location = new Point(76, 514);
            _sourceLabel.Size = new Size(684, 20);
            Controls.Add(_sourceLabel);

            _selectButton = new Button();
            _selectButton.Text = "Select and Add";
            _selectButton.Location = new Point(542, 548);
            _selectButton.Size = new Size(110, 32);
            _selectButton.Enabled = false;
            _selectButton.Click += delegate { AcceptSelection(); };
            Controls.Add(_selectButton);

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Location = new Point(662, 548);
            cancelButton.Size = new Size(98, 32);
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);

            AcceptButton = _searchButton;
            CancelButton = cancelButton;

            FormClosed += delegate { _searchDebounceTimer.Dispose(); };

            Shown += delegate
            {
                _searchBox.Focus();
                _searchBox.SelectAll();
                if (_initialQuery.Length > 0 || _scope != SoftwareSearchScope.Global)
                {
                    Search();
                }
            };
        }

        private void Search()
        {
            string query = _searchBox.Text.Trim();
            if (query.Length == 0 && _scope == SoftwareSearchScope.Global)
            {
                PopulateResults(new List<SearchResult>());
                _statusLabel.Text = "Enter part of a program name, such as “CS2” or a Steam title.";
                _statusLabel.ForeColor = Color.DarkOrange;
                return;
            }

            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            _searchButton.Enabled = false;
            try
            {
                List<SearchResult> results = FindSoftware(query, _scope);
                PopulateResults(results);
                if (results.Count == 0)
                {
                    _statusLabel.Text = _scope == SoftwareSearchScope.Desktop
                        ? "No matching desktop shortcuts or EXEs found."
                        : "No matches found. Try another name or Desktop Search.";
                    _statusLabel.ForeColor = Color.DarkOrange;
                }
                else
                {
                    _statusLabel.Text = "Found " + results.Count + " matches. Select an icon and click “Select and Add”.";
                    _statusLabel.ForeColor = Color.DarkGreen;
                }
            }
            catch (Exception exception)
            {
                _statusLabel.Text = "Search failed: " + exception.Message;
                _statusLabel.ForeColor = Color.Firebrick;
            }
            finally
            {
                _searchButton.Enabled = true;
                Cursor = previousCursor;
            }
        }

        private static List<SearchResult> FindSoftware(string query, SoftwareSearchScope scope)
        {
            Dictionary<string, SearchResult> candidates =
                new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);

            if (scope == SoftwareSearchScope.Desktop)
            {
                AddDesktopApplications(candidates);
            }
            else if (scope == SoftwareSearchScope.Running)
            {
                AddRunningApplications(candidates);
            }
            else
            {
                AddRunningApplications(candidates);
                AddStartMenuApplications(candidates);
                AddSteamApplications(candidates, query);
                AddEpicApplications(candidates, query);
                AddInstalledApplications(candidates, query);
                AddAppPathApplications(candidates);
            }

            List<SearchResult> matches = new List<SearchResult>();
            foreach (SearchResult candidate in candidates.Values)
            {
                candidate.Score = GetMatchScore(candidate, query);
                if (candidate.Score >= 0)
                {
                    matches.Add(candidate);
                }
            }

            matches.Sort(delegate(SearchResult left, SearchResult right)
            {
                int scoreComparison = right.Score.CompareTo(left.Score);
                if (scoreComparison != 0)
                {
                    return scoreComparison;
                }
                return string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });

            if (matches.Count > 30)
            {
                matches.RemoveRange(30, matches.Count - 30);
            }
            return matches;
        }

        private static void AddDesktopApplications(Dictionary<string, SearchResult> candidates)
        {
            List<string> roots = new List<string>();
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            object shell = null;
            try
            {
                if (shellType != null)
                {
                    shell = Activator.CreateInstance(shellType);
                }

                foreach (string root in roots)
                {
                    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    {
                        continue;
                    }

                    try
                    {
                        foreach (string filePath in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                        {
                            bool insideFolder = !string.Equals(
                                Path.GetDirectoryName(filePath),
                                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                                StringComparison.OrdinalIgnoreCase);

                            string extension = Path.GetExtension(filePath);
                            if (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                if (!IsExcludedUtilityEntry(Path.GetFileNameWithoutExtension(filePath), filePath))
                                {
                                    AddCandidate(
                                        candidates,
                                        Path.GetFileNameWithoutExtension(filePath),
                                        filePath,
                                        insideFolder ? "Desktop Folder" : "Desktop Program",
                                        false);
                                }
                            }
                            else if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase) && shell != null)
                            {
                                AddShortcutCandidate(
                                    candidates,
                                    shellType,
                                    shell,
                                    filePath,
                                    insideFolder ? "Desktop Folder" : "Desktop Shortcut");
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                ReleaseComObject(shell);
            }
        }
        private static void AddRunningApplications(Dictionary<string, SearchResult> candidates)
        {
            int currentProcessId;
            using (Process currentProcess = Process.GetCurrentProcess())
            {
                currentProcessId = currentProcess.Id;
            }

            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == currentProcessId ||
                        process.MainWindowHandle == IntPtr.Zero ||
                        IsExcludedRunningProcess(process.ProcessName + ".exe"))
                    {
                        continue;
                    }

                    string executablePath = process.MainModule.FileName;
                    if (!IsExecutablePath(executablePath))
                    {
                        continue;
                    }

                    string displayName = process.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = Path.GetFileNameWithoutExtension(executablePath);
                    }
                    else
                    {
                        displayName = displayName.Trim();
                    }

                    AddCandidate(candidates, displayName, executablePath, "Running", true);
                }
                catch
                {
                    // Protected or exiting processes are ignored.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static bool IsExcludedRunningProcess(string processName)
        {
            string[] excluded = new string[]
            {
                "explorer.exe",
                "dwm.exe",
                "shellexperiencehost.exe",
                "startmenuexperiencehost.exe",
                "searchhost.exe",
                "textinputhost.exe",
                "applicationframehost.exe",
                "systemsettings.exe",
                "taskmgr.exe",
                "lockapp.exe",
                "logonui.exe"
            };
            foreach (string name in excluded)
            {
                if (string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        private static void AddStartMenuApplications(Dictionary<string, SearchResult> candidates)
        {
            List<string> roots = new List<string>();
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu));
            roots.Add(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu));

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return;
            }

            object shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                foreach (string root in roots)
                {
                    if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                    {
                        continue;
                    }

                    try
                    {
                        foreach (string shortcutPath in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
                        {
                            AddShortcutCandidate(candidates, shellType, shell, shortcutPath, "Start Menu");
                        }
                    }
                    catch
                    {
                        // One inaccessible Start Menu subtree should not stop the search.
                    }
                }
            }
            finally
            {
                ReleaseComObject(shell);
            }
        }

        private static void AddShortcutCandidate(
            Dictionary<string, SearchResult> candidates,
            Type shellType,
            object shell,
            string shortcutPath,
            string source)
        {
            object shortcut = null;
            try
            {
                shortcut = shellType.InvokeMember(
                    "CreateShortcut",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { shortcutPath });

                string targetPath = shortcut.GetType().InvokeMember(
                    "TargetPath",
                    BindingFlags.GetProperty,
                    null,
                    shortcut,
                    null) as string;

                if (!IsExecutablePath(targetPath))
                {
                    return;
                }

                string displayName = Path.GetFileNameWithoutExtension(shortcutPath);
                if (IsExcludedUtilityEntry(displayName, targetPath))
                {
                    return;
                }
                AddCandidate(candidates, displayName, targetPath, source, false);
            }
            catch
            {
                // Broken or unsupported shortcuts are ignored.
            }
            finally
            {
                ReleaseComObject(shortcut);
            }
        }

        private static void AddEpicApplications(Dictionary<string, SearchResult> candidates, string query)
        {
            if (NormalizeForSearch(query).Length < 1)
            {
                return;
            }

            JavaScriptSerializer serializer = new JavaScriptSerializer();
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            string manifestDirectory = Path.Combine(programData, "Epic", "EpicGamesLauncher", "Data", "Manifests");
            int matchedApps = 0;

            if (Directory.Exists(manifestDirectory))
            {
                string[] manifestFiles;
                try
                {
                    manifestFiles = Directory.GetFiles(manifestDirectory, "*.item", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    manifestFiles = new string[0];
                }

                foreach (string manifestFile in manifestFiles)
                {
                    try
                    {
                        string json = File.ReadAllText(manifestFile);
                        Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(json);
                        if (data == null)
                        {
                            continue;
                        }

                        string displayName = GetJsonString(data, "DisplayName");
                        string installLocation = GetJsonString(data, "InstallLocation");
                        string launchExecutable = GetJsonString(data, "LaunchExecutable");
                        if (string.IsNullOrWhiteSpace(displayName) ||
                            string.IsNullOrWhiteSpace(installLocation) ||
                            GetTextMatchScore(displayName, installLocation, query) < 0)
                        {
                            continue;
                        }

                        string executablePath = launchExecutable;
                        if (!string.IsNullOrWhiteSpace(executablePath) && !Path.IsPathRooted(executablePath))
                        {
                            executablePath = Path.Combine(installLocation, executablePath);
                        }
                        executablePath = CleanExecutablePath(executablePath);

                        if (IsExecutablePath(executablePath))
                        {
                            AddCandidate(candidates, displayName, executablePath, "Epic Library", false);
                            matchedApps++;
                        }
                        else if (Directory.Exists(installLocation))
                        {
                            List<SteamExecutableCandidate> executables = FindSteamExecutables(
                                displayName,
                                Path.GetFileName(installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                                installLocation);
                            int takeCount = Math.Min(2, executables.Count);
                            for (int index = 0; index < takeCount; index++)
                            {
                                AddCandidate(candidates, displayName, executables[index].ExecutablePath, "Epic Library", false);
                            }
                            if (takeCount > 0)
                            {
                                matchedApps++;
                            }
                        }

                        if (matchedApps >= 10)
                        {
                            return;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            string installedFile = Path.Combine(programData, "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (!File.Exists(installedFile) || matchedApps >= 10)
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(installedFile);
                Dictionary<string, object> data = serializer.Deserialize<Dictionary<string, object>>(json);
                object[] installations = GetJsonArray(data, "InstallationList");
                if (installations == null)
                {
                    return;
                }

                foreach (object item in installations)
                {
                    Dictionary<string, object> installation = item as Dictionary<string, object>;
                    if (installation == null)
                    {
                        continue;
                    }

                    string appName = GetJsonString(installation, "AppName");
                    string installLocation = GetJsonString(installation, "InstallLocation");
                    if (string.IsNullOrWhiteSpace(appName) ||
                        string.IsNullOrWhiteSpace(installLocation) ||
                        !Directory.Exists(installLocation) ||
                        GetTextMatchScore(appName, installLocation, query) < 0)
                    {
                        continue;
                    }

                    List<SteamExecutableCandidate> executables = FindSteamExecutables(
                        appName,
                        Path.GetFileName(installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                        installLocation);
                    if (executables.Count > 0)
                    {
                        AddCandidate(candidates, appName, executables[0].ExecutablePath, "Epic Library", false);
                        matchedApps++;
                        if (matchedApps >= 10)
                        {
                            return;
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private static void AddInstalledApplications(Dictionary<string, SearchResult> candidates, string query)
        {
            if (NormalizeForSearch(query).Length < 1)
            {
                return;
            }

            string[] registryPaths = new string[]
            {
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Uninstall",
                @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (string registryPath in registryPaths)
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(
                        registryPath.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase)
                            ? RegistryHive.CurrentUser
                            : RegistryHive.LocalMachine,
                        RegistryView.Default))
                    using (RegistryKey uninstall = root.OpenSubKey(
                        registryPath.Substring(registryPath.IndexOf('\\') + 1)))
                    {
                        if (uninstall == null)
                        {
                            continue;
                        }

                        foreach (string subKeyName in uninstall.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey appKey = uninstall.OpenSubKey(subKeyName))
                                {
                                    if (appKey == null)
                                    {
                                        continue;
                                    }

                                    string displayName = appKey.GetValue("DisplayName") as string;
                                    string installLocation = appKey.GetValue("InstallLocation") as string;
                                    if (string.IsNullOrWhiteSpace(displayName) ||
                                        IsExcludedUtilityEntry(displayName, installLocation) ||
                                        GetTextMatchScore(displayName, installLocation, query) < 0)
                                    {
                                        continue;
                                    }

                                    string executablePath = CleanExecutablePath(appKey.GetValue("DisplayIcon") as string);
                                    if (IsExecutablePath(executablePath) && !IsExcludedUtilityEntry(displayName, executablePath))
                                    {
                                        AddCandidate(candidates, displayName, executablePath, "Installed Programs", false);
                                        continue;
                                    }

                                    if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                                    {
                                        List<SteamExecutableCandidate> executables = FindSteamExecutables(
                                            displayName,
                                            Path.GetFileName(installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                                            installLocation);
                                        if (executables.Count > 0)
                                        {
                                            AddCandidate(candidates, displayName, executables[0].ExecutablePath, "Installed Programs", false);
                                        }
                                    }
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static string GetJsonString(Dictionary<string, object> data, string key)
        {
            if (data == null)
            {
                return null;
            }

            foreach (KeyValuePair<string, object> pair in data)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value as string;
                }
            }
            return null;
        }

        private static object[] GetJsonArray(Dictionary<string, object> data, string key)
        {
            if (data == null)
            {
                return null;
            }

            foreach (KeyValuePair<string, object> pair in data)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    return pair.Value as object[];
                }
            }
            return null;
        }
        private static void AddSteamApplications(Dictionary<string, SearchResult> candidates, string query)
        {
            string normalizedQuery = NormalizeForSearch(query);
            if (normalizedQuery.Length < 1)
            {
                return;
            }

            List<string> libraryRoots = FindSteamLibraryRoots();
            int matchedApps = 0;

            foreach (string libraryRoot in libraryRoots)
            {
                string steamApps = Path.Combine(libraryRoot, "steamapps");
                string commonDirectory = Path.Combine(steamApps, "common");
                if (!Directory.Exists(steamApps) || !Directory.Exists(commonDirectory))
                {
                    continue;
                }

                string[] manifests;
                try
                {
                    manifests = Directory.GetFiles(steamApps, "appmanifest_*.acf", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (string manifestPath in manifests)
                {
                    try
                    {
                        string manifest = File.ReadAllText(manifestPath);
                        string gameName = ReadVdfValue(manifest, "name");
                        string installDirectory = ReadVdfValue(manifest, "installdir");
                        if (string.IsNullOrWhiteSpace(gameName) || string.IsNullOrWhiteSpace(installDirectory))
                        {
                            continue;
                        }

                        if (gameName.IndexOf("Redistributables", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        if (GetTextMatchScore(gameName, installDirectory, query) < 0)
                        {
                            continue;
                        }

                        string gameDirectory = Path.Combine(commonDirectory, installDirectory);
                        if (!Directory.Exists(gameDirectory))
                        {
                            continue;
                        }

                        List<SteamExecutableCandidate> executables = FindSteamExecutables(gameName, installDirectory, gameDirectory);
                        if (executables.Count == 0)
                        {
                            continue;
                        }

                        int takeCount = Math.Min(2, executables.Count);
                        bool duplicateFileNames = takeCount > 1 &&
                            string.Equals(
                                Path.GetFileName(executables[0].ExecutablePath),
                                Path.GetFileName(executables[1].ExecutablePath),
                                StringComparison.OrdinalIgnoreCase);

                        for (int index = 0; index < takeCount; index++)
                        {
                            SteamExecutableCandidate executable = executables[index];
                            string displayName = gameName;
                            if (takeCount > 1)
                            {
                                string suffix;
                                if (duplicateFileNames)
                                {
                                    suffix = executable.ExecutablePath.Substring(gameDirectory.Length)
                                        .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                }
                                else
                                {
                                    suffix = Path.GetFileNameWithoutExtension(executable.ExecutablePath);
                                }
                                displayName += " - " + suffix;
                            }
                            AddCandidate(candidates, displayName, executable.ExecutablePath, "Steam Library", false);
                        }

                        matchedApps++;
                        if (matchedApps >= 10)
                        {
                            return;
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static List<string> FindSteamLibraryRoots()
        {
            List<string> roots = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string steamPath = GetSteamInstallPath();
            if (!string.IsNullOrWhiteSpace(steamPath))
            {
                AddSteamRoot(roots, seen, steamPath);
                string libraryFile = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(libraryFile))
                {
                    try
                    {
                        string content = File.ReadAllText(libraryFile);
                        MatchCollection pathMatches = Regex.Matches(content, "\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        foreach (Match match in pathMatches)
                        {
                            AddSteamRoot(roots, seen, UnescapeVdf(match.Groups[1].Value));
                        }

                        // Older Steam clients stored library paths as numeric key/value pairs.
                        MatchCollection legacyMatches = Regex.Matches(content, "\"\\d+\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase);
                        foreach (Match match in legacyMatches)
                        {
                            string value = UnescapeVdf(match.Groups[1].Value);
                            if (Directory.Exists(value))
                            {
                                AddSteamRoot(roots, seen, value);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return roots;
        }

        private static string GetSteamInstallPath()
        {
            string[] userPaths = new string[]
            {
                @"Software\Valve\Steam"
            };
            foreach (string path in userPaths)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path))
                    {
                        string value = key == null ? null : key.GetValue("SteamPath") as string;
                        if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                        {
                            return value;
                        }
                    }
                }
                catch
                {
                }
            }

            string[] machinePaths = new string[]
            {
                @"SOFTWARE\WOW6432Node\Valve\Steam",
                @"SOFTWARE\Valve\Steam"
            };
            foreach (string path in machinePaths)
            {
                try
                {
                    using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path))
                    {
                        string value = key == null ? null : key.GetValue("InstallPath") as string;
                        if (!string.IsNullOrWhiteSpace(value) && Directory.Exists(value))
                        {
                            return value;
                        }
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static void AddSteamRoot(List<string> roots, HashSet<string> seen, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                string fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
                if (Directory.Exists(fullPath) && seen.Add(fullPath))
                {
                    roots.Add(fullPath);
                }
            }
            catch
            {
            }
        }

        private static List<SteamExecutableCandidate> FindSteamExecutables(
            string gameName,
            string installDirectory,
            string gameDirectory)
        {
            List<SteamExecutableCandidate> executables = new List<SteamExecutableCandidate>();
            try
            {
                foreach (string executablePath in Directory.EnumerateFiles(gameDirectory, "*.exe", SearchOption.AllDirectories))
                {
                    string fileName = Path.GetFileNameWithoutExtension(executablePath);
                    if (IsExcludedSteamExecutable(fileName))
                    {
                        continue;
                    }

                    long length = 0;
                    try
                    {
                        length = new FileInfo(executablePath).Length;
                    }
                    catch
                    {
                    }

                    SteamExecutableCandidate candidate = new SteamExecutableCandidate();
                    candidate.ExecutablePath = executablePath;
                    candidate.Score = GetSteamExecutableScore(gameName, installDirectory, gameDirectory, executablePath, fileName, length);
                    candidate.Length = length;
                    executables.Add(candidate);
                }
            }
            catch
            {
            }

            executables.Sort(delegate(SteamExecutableCandidate left, SteamExecutableCandidate right)
            {
                int scoreComparison = right.Score.CompareTo(left.Score);
                if (scoreComparison != 0)
                {
                    return scoreComparison;
                }
                return right.Length.CompareTo(left.Length);
            });
            return executables;
        }

        private static bool IsExcludedSteamExecutable(string fileName)
        {
            string value = fileName.ToLowerInvariant();
            string[] blockedFragments = new string[]
            {
                "unins", "uninstall", "setup", "redist", "vcredist", "dxsetup",
                "crash", "report", "diagnostic", "config", "editor", "sdk",
                "modmanager", "updater", "inject", "service", "vconsole",
                "legacy", "benchmark"
            };
            foreach (string fragment in blockedFragments)
            {
                if (value.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static int GetSteamExecutableScore(
            string gameName,
            string installDirectory,
            string gameDirectory,
            string executablePath,
            string fileName,
            long length)
        {
            string normalizedGame = NormalizeForSearch(gameName);
            string normalizedInstall = NormalizeForSearch(installDirectory);
            string normalizedFile = NormalizeForSearch(fileName);
            int score = 0;

            if (normalizedFile == normalizedGame || normalizedFile == normalizedInstall)
            {
                score += 120;
            }
            else if ((normalizedGame.Length >= 3 && normalizedFile.IndexOf(normalizedGame, StringComparison.Ordinal) >= 0) ||
                     (normalizedInstall.Length >= 3 && normalizedFile.IndexOf(normalizedInstall, StringComparison.Ordinal) >= 0) ||
                     (normalizedFile.Length >= 3 && normalizedGame.IndexOf(normalizedFile, StringComparison.Ordinal) >= 0))
            {
                score += 70;
            }
            else
            {
                string[] gameTokens = normalizedGame.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string token in gameTokens)
                {
                    if (token.Length >= 4 && normalizedFile.IndexOf(token, StringComparison.Ordinal) >= 0)
                    {
                        score += 30;
                        break;
                    }
                }
            }

            if (normalizedFile.IndexOf("shipping", StringComparison.Ordinal) >= 0) { score += 35; }
            if (normalizedFile.IndexOf("game", StringComparison.Ordinal) >= 0) { score += 25; }
            if (normalizedFile.IndexOf("win64", StringComparison.Ordinal) >= 0 ||
                normalizedFile.EndsWith("64", StringComparison.Ordinal)) { score += 20; }
            if (normalizedFile.IndexOf("launcher", StringComparison.Ordinal) >= 0) { score += 12; }
            if (normalizedFile.EndsWith("32", StringComparison.Ordinal)) { score -= 10; }

            try
            {
                string relativePath = executablePath.Substring(gameDirectory.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                int depth = relativePath.Split(new char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries).Length - 1;
                score += Math.Max(0, 18 - depth * 4);
            }
            catch
            {
            }

            if (length >= 1024 * 1024)
            {
                score += 10;
            }
            return score;
        }

        private static string ReadVdfValue(string content, string key)
        {
            Match match = Regex.Match(
                content,
                "\"" + Regex.Escape(key) + "\"\\s+\"([^\"]+)\"",
                RegexOptions.IgnoreCase);
            return match.Success ? UnescapeVdf(match.Groups[1].Value) : null;
        }

        private static string UnescapeVdf(string value)
        {
            return value.Replace("\\\\", "\\");
        }

        private static int GetTextMatchScore(string primary, string secondary, string query)
        {
            string normalizedQuery = NormalizeForSearch(query);
            string normalizedPrimary = NormalizeForSearch(primary);
            string normalizedSecondary = NormalizeForSearch(secondary);
            if (normalizedPrimary == normalizedQuery || normalizedSecondary == normalizedQuery)
            {
                return 100;
            }
            if (normalizedPrimary.StartsWith(normalizedQuery, StringComparison.Ordinal) ||
                normalizedSecondary.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                return 80;
            }
            if (normalizedPrimary.IndexOf(normalizedQuery, StringComparison.Ordinal) >= 0 ||
                normalizedSecondary.IndexOf(normalizedQuery, StringComparison.Ordinal) >= 0)
            {
                return 60;
            }
            if (normalizedQuery == GetAcronym(normalizedPrimary) ||
                normalizedQuery == GetAcronym(normalizedSecondary))
            {
                return 55;
            }
            return -1;
        }

        private static string NormalizeForSearch(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            bool previousWasSpace = true;
            foreach (char character in value.ToLowerInvariant())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(character);
                    previousWasSpace = false;
                }
                else if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }
            }
            return builder.ToString().Trim();
        }

        private static string GetAcronym(string normalizedValue)
        {
            string[] tokens = normalizedValue.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            StringBuilder builder = new StringBuilder();
            foreach (string token in tokens)
            {
                builder.Append(token[0]);
            }
            return builder.ToString();
        }
        private static void AddAppPathApplications(Dictionary<string, SearchResult> candidates)
        {
            RegistryKey[] roots = new RegistryKey[] { Registry.LocalMachine, Registry.CurrentUser };
            foreach (RegistryKey root in roots)
            {
                try
                {
                    using (RegistryKey appPaths = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
                    {
                        if (appPaths == null)
                        {
                            continue;
                        }

                        foreach (string subKeyName in appPaths.GetSubKeyNames())
                        {
                            try
                            {
                                using (RegistryKey appKey = appPaths.OpenSubKey(subKeyName))
                                {
                                    if (appKey == null)
                                    {
                                        continue;
                                    }

                                    string rawPath = appKey.GetValue(string.Empty) as string;
                                    string executablePath = CleanExecutablePath(rawPath);
                                    if (!IsExecutablePath(executablePath))
                                    {
                                        continue;
                                    }

                                    string displayName = Path.GetFileNameWithoutExtension(subKeyName);
                                    AddCandidate(candidates, displayName, executablePath, "Windows App Paths", false);
                                }
                            }
                            catch
                            {
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static bool IsExcludedUtilityEntry(string displayName, string executablePath)
        {
            string name = (displayName ?? string.Empty).ToLowerInvariant();
            string file = Path.GetFileNameWithoutExtension(executablePath ?? string.Empty).ToLowerInvariant();
            string[] fragments = new string[]
            {
                "Uninstall", "uninstall", "unins", "unwise", "remove "
            };
            foreach (string fragment in fragments)
            {
                if (name.IndexOf(fragment, StringComparison.Ordinal) >= 0 ||
                    file.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
        private static void AddCandidate(
            Dictionary<string, SearchResult> candidates,
            string displayName,
            string executablePath,
            string source,
            bool isRunning)
        {
            if (string.IsNullOrWhiteSpace(displayName) || !IsExecutablePath(executablePath))
            {
                return;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(executablePath);
            }
            catch
            {
                return;
            }

            SearchResult existing;
            if (candidates.TryGetValue(fullPath, out existing))
            {
                existing.IsRunning = existing.IsRunning || isRunning;
                return;
            }

            SearchResult result = new SearchResult();
            result.DisplayName = displayName.Trim();
            result.ExecutablePath = fullPath;
            result.ProcessName = Path.GetFileName(fullPath);
            result.Source = source;
            result.IsRunning = isRunning;
            candidates[fullPath] = result;
        }

        private static bool IsExecutablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                return expanded.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(expanded);
            }
            catch
            {
                return false;
            }
        }

        private static string CleanExecutablePath(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return null;
            }

            string value = Environment.ExpandEnvironmentVariables(rawPath.Trim());
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int closingQuote = value.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    return value.Substring(1, closingQuote - 1);
                }
            }

            int exeIndex = value.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeIndex >= 0)
            {
                return value.Substring(0, exeIndex + 4).Trim('"');
            }
            return value.Trim('"');
        }

        private static int GetMatchScore(SearchResult result, string query)
        {
            string displayName = result.DisplayName;
            string fileName = Path.GetFileNameWithoutExtension(result.ProcessName);
            int score = GetTextMatchScore(displayName, fileName, query);
            if (score < 0)
            {
                string normalizedQuery = NormalizeForSearch(query);
                string normalizedPath = NormalizeForSearch(result.ExecutablePath);
                if (normalizedQuery.Length > 0 && normalizedPath.IndexOf(normalizedQuery, StringComparison.Ordinal) >= 0)
                {
                    score = 30;
                }
            }

            if (score >= 0 && result.IsRunning)
            {
                score += 3;
            }
            return score;
        }

        private void PopulateResults(List<SearchResult> results)
        {
            foreach (Image image in _iconList.Images)
            {
                image.Dispose();
            }
            _iconList.Images.Clear();
            _resultList.Items.Clear();
            _pathLabel.Text = "Nothing selected";
            _sourceLabel.Text = "—";
            _selectButton.Enabled = false;

            foreach (SearchResult result in results)
            {
                Bitmap bitmap = CreateIconBitmap(result);
                int imageIndex = _iconList.Images.Count;
                _iconList.Images.Add(bitmap);

                string itemText = result.DisplayName;
                if (result.IsRunning)
                {
                    itemText += " (Running)";
                }

                ListViewItem item = new ListViewItem(itemText);
                item.ImageIndex = imageIndex;
                item.Tag = result;
                item.ToolTipText = result.ExecutablePath + Environment.NewLine + "Source: " + result.Source;
                _resultList.Items.Add(item);
            }

            if (_resultList.Items.Count > 0)
            {
                _resultList.Items[0].Selected = true;
                _resultList.Items[0].Focused = true;
            }
        }

        private void UpdateSelectionDetails()
        {
            if (_resultList.SelectedItems.Count == 0)
            {
                _pathLabel.Text = "Nothing selected";
                _sourceLabel.Text = "—";
                _selectButton.Enabled = false;
                return;
            }

            SearchResult result = _resultList.SelectedItems[0].Tag as SearchResult;
            if (result == null)
            {
                return;
            }
            _pathLabel.Text = result.ExecutablePath;
            _sourceLabel.Text = result.Source + (result.IsRunning ? " / Currently running" : string.Empty);
            _selectButton.Enabled = true;
        }

        private static Bitmap CreateIconBitmap(SearchResult result)
        {
            Bitmap bitmap = new Bitmap(64, 64, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.Clear(Color.Transparent);

                Icon icon = null;
                bool disposeIcon = false;
                try
                {
                    icon = Icon.ExtractAssociatedIcon(result.ExecutablePath);
                    disposeIcon = icon != null;
                }
                catch
                {
                }

                if (icon == null)
                {
                    icon = SystemIcons.Application;
                }

                try
                {
                    graphics.DrawIcon(icon, new Rectangle(2, 2, 60, 60));
                }
                finally
                {
                    if (disposeIcon && icon != null)
                    {
                        icon.Dispose();
                    }
                }

                if (result.IsRunning)
                {
                    using (Brush borderBrush = new SolidBrush(Color.White))
                    using (Brush runningBrush = new SolidBrush(Color.FromArgb(34, 197, 94)))
                    {
                        graphics.FillEllipse(borderBrush, 46, 0, 18, 18);
                        graphics.FillEllipse(runningBrush, 49, 3, 12, 12);
                    }
                }
            }
            return bitmap;
        }

        private void AcceptSelection()
        {
            if (_resultList.SelectedItems.Count == 0)
            {
                return;
            }

            SearchResult result = _resultList.SelectedItems[0].Tag as SearchResult;
            if (result == null)
            {
                return;
            }

            SelectedProcessName = result.ProcessName;
            SelectedExecutablePath = result.ExecutablePath;
            DialogResult = DialogResult.OK;
            Close();
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
            {
                try
                {
                    Marshal.FinalReleaseComObject(value);
                }
                catch
                {
                }
            }
        }

        private sealed class SearchResult
        {
            public string DisplayName { get; set; }
            public string ExecutablePath { get; set; }
            public string ProcessName { get; set; }
            public string Source { get; set; }
            public bool IsRunning { get; set; }
            public int Score { get; set; }
        }

        private sealed class SteamExecutableCandidate
        {
            public string ExecutablePath { get; set; }
            public int Score { get; set; }
            public long Length { get; set; }
        }
    }
}
