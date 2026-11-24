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
using System.Xml;
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
        private readonly Button _refreshButton;
        private readonly Label _statusLabel;
        private readonly ListView _resultList;
        private readonly Label _pathLabel;
        private readonly Label _sourceLabel;
        private readonly ImageList _iconList;
        private readonly Button _selectButton;
        private readonly Timer _searchDebounceTimer;
        private readonly Timer _indexPollTimer;
        private readonly string _initialQuery;
        private readonly SoftwareSearchScope _scope;
        private static readonly object FullDriveIndexLock = new object();
        private static List<SearchResult> _fullDriveIndex;
        private static bool _fullDriveIndexStarted;
        private static bool _fullDriveIndexCompleted;
        private static string _fullDriveIndexStatus = string.Empty;
        private static readonly object WindowsStoreIndexLock = new object();
        private static List<SearchResult> _windowsStoreApplications;

        public string SelectedProcessName { get; private set; }
        public string SelectedExecutablePath { get; private set; }

        public SoftwareSearchForm(string initialQuery, SoftwareSearchScope scope)
        {
            _initialQuery = initialQuery == null ? string.Empty : initialQuery.Trim();
            _scope = scope;

            Text = _scope == SoftwareSearchScope.Desktop
                ? "Desktop Search"
                : (_scope == SoftwareSearchScope.Running ? "Add Running" : "Global Search");
            Font = SystemFonts.MessageBoxFont;
            UiAutoScale.Prepare(this);
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
            titleLabel.Font = new Font(Font.FontFamily, Math.Min(Font.Size + 7F, 24F), FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.Location = new Point(18, 14);
            titleLabel.Size = new Size(500, 34);
            Controls.Add(titleLabel);

            _searchBox = new TextBox();
            _searchBox.Location = new Point(20, 56);
            _searchBox.Size = new Size(600, 27);
            _searchBox.Text = _initialQuery;
            _searchBox.KeyDown += delegate(object sender, KeyEventArgs eventArgs)
            {
                if (eventArgs.KeyCode == Keys.Enter || eventArgs.KeyCode == Keys.F5)
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

            _indexPollTimer = new Timer();
            _indexPollTimer.Interval = 400;
            _indexPollTimer.Tick += delegate
            {
                if (!IsFullDriveIndexCompleted())
                {
                    return;
                }

                _indexPollTimer.Stop();
                Search();
            };

            _refreshButton = null;
            _searchButton = new Button();
            _searchButton.Text = "Search";
            _searchButton.Location = new Point(630, 54);
            _searchButton.Size = new Size(130, 29);
            _searchButton.Click += delegate { Search(); };
            Controls.Add(_searchButton);

            if (_scope == SoftwareSearchScope.Running)
            {
                _searchBox.Width = 520;
                _searchButton.Location = new Point(550, 54);
                _searchButton.Width = 95;

                _refreshButton = new Button();
                _refreshButton.Text = "Refresh";
                _refreshButton.Location = new Point(655, 54);
                _refreshButton.Size = new Size(105, 29);
                _refreshButton.Click += delegate { Search(); };
                Controls.Add(_refreshButton);
            }

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

            FormClosed += delegate { _searchDebounceTimer.Dispose(); _indexPollTimer.Dispose(); };

            Shown += delegate
            {
                _searchBox.Focus();
                _searchBox.SelectAll();
                if (_scope == SoftwareSearchScope.Global || _scope == SoftwareSearchScope.Desktop)
                {
                    EnsureFullDriveIndexStarted();
                    if (!IsFullDriveIndexCompleted())
                    {
                        _indexPollTimer.Start();
                    }
                }
                if (_initialQuery.Length > 0 || _scope != SoftwareSearchScope.Global)
                {
                    Search();
                }
            };
            UiAutoScale.Enable(this);
        }

        private void Search()
        {
            string query = _searchBox.Text.Trim();
            if (query.Length == 0 && _scope == SoftwareSearchScope.Global)
            {
                PopulateResults(new List<SearchResult>());
                _statusLabel.Text = "Enter a program or game name. Applications from any game launcher are supported.";
                _statusLabel.ForeColor = Color.DarkOrange;
                return;
            }

            Cursor previousCursor = Cursor;
            Cursor = Cursors.WaitCursor;
            _searchButton.Enabled = false;
            if (_refreshButton != null)
            {
                _refreshButton.Enabled = false;
            }
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
                    string indexStatus = GetFullDriveIndexStatusText();
                    _statusLabel.Text = "Found " + results.Count + " matches. Select an icon and click “Select and Add”." +
                        (indexStatus.Length == 0 ? string.Empty : " " + indexStatus);
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
                if (_refreshButton != null)
                {
                    _refreshButton.Enabled = true;
                }
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
                AddDesktopApplications(candidates);
                AddStartMenuApplications(candidates);
                AddSteamApplications(candidates, query);
                AddEpicApplications(candidates, query);
                AddInstalledApplications(candidates, query);
                AddAppPathApplications(candidates);
                AddWindowsStoreApplications(candidates, query);
            }

            if (scope == SoftwareSearchScope.Global &&
                !HasMatchingCandidate(candidates, query))
            {
                AddFullDriveApplications(candidates, query);
            }

            AddLauncherMainApplications(candidates);

            List<SearchResult> matches = new List<SearchResult>();
            foreach (SearchResult candidate in candidates.Values)
            {
                if (scope != SoftwareSearchScope.Desktop &&
                    IsInstallerOrMaintenanceApplication(candidate))
                {
                    continue;
                }

                if (scope == SoftwareSearchScope.Global &&
                    (IsBackgroundSearchResult(candidate) ||
                     IsBuiltInWindowsApplication(candidate)))
                {
                    continue;
                }

                candidate.Score = GetMatchScore(candidate, query);
                if (candidate.Score >= 0)
                {
                    matches.Add(candidate);
                }
            }

            matches = CollapseDuplicateApplications(matches, scope);
            matches.Sort(delegate(SearchResult left, SearchResult right)
            {
                int scoreComparison = right.Score.CompareTo(left.Score);
                if (scoreComparison != 0)
                {
                    return scoreComparison;
                }
                return string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });

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
                        foreach (string filePath in EnumerateFilesSafely(root))
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
                            else if (string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase))
                            {
                                AddUrlShortcutCandidate(
                                    candidates,
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
                        IsExcludedRunningProcess(process.ProcessName + ".exe"))
                    {
                        continue;
                    }

                    IntPtr mainWindowHandle = process.MainWindowHandle;
                    if (!NativeMethods.IsMainWindowVisible(mainWindowHandle))
                    {
                        continue;
                    }

                    bool hasMainWindow = true;
                    string processFileName = process.ProcessName + ".exe";
                    string displayName = process.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(displayName))
                    {
                        displayName = Path.GetFileNameWithoutExtension(processFileName);
                    }
                    else
                    {
                        displayName = displayName.Trim();
                    }

                    string executablePath = null;
                    try
                    {
                        executablePath = process.MainModule.FileName;
                    }
                    catch
                    {
                    }

                    if (IsExecutablePath(executablePath))
                    {
                        AddCandidate(candidates, displayName, executablePath, "Running", true, hasMainWindow);
                    }
                    else
                    {
                        AddProcessOnlyCandidate(
                            candidates,
                            displayName,
                            processFileName,
                            "Running",
                            hasMainWindow);
                    }
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
                "tokeime.exe",
                "explorer.exe",
                "dwm.exe",
                "shellexperiencehost.exe",
                "startmenuexperiencehost.exe",
                "searchhost.exe",
                "tabtip.exe",
                "textinputhost.exe",
                "applicationframehost.exe",
                "nvidia overlay.exe",
                "nvcontainer.exe",
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

                    foreach (string shortcutPath in EnumerateFilesSafely(root))
                    {
                        string extension = Path.GetExtension(shortcutPath);
                        if (string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            AddShortcutCandidate(candidates, shellType, shell, shortcutPath, "Start Menu");
                        }
                        else if (string.Equals(extension, ".url", StringComparison.OrdinalIgnoreCase))
                        {
                            AddUrlShortcutCandidate(candidates, shortcutPath, "Start Menu");
                        }
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
                    try
                    {
                        string iconLocation = shortcut.GetType().InvokeMember(
                            "IconLocation",
                            BindingFlags.GetProperty,
                            null,
                            shortcut,
                            null) as string;
                        string iconPath = CleanExecutablePath(iconLocation);
                        if (IsExecutablePath(iconPath))
                        {
                            targetPath = iconPath;
                        }
                    }
                    catch
                    {
                    }
                }

                if (!IsExecutablePath(targetPath))
                {
                    TryResolveShortcutFromIndex(candidates, shortcutPath, source);
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
                            string.IsNullOrWhiteSpace(installLocation))
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
                        }
                        else if (Directory.Exists(installLocation))
                        {
                            List<SteamExecutableCandidate> executables = FindSteamExecutables(
                                displayName,
                                Path.GetFileName(installLocation.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                                installLocation);
                            int takeCount = Math.Min(1, executables.Count);
                            for (int index = 0; index < takeCount; index++)
                            {
                                AddCandidate(candidates, displayName, executables[index].ExecutablePath, "Epic Library", false);
                            }
                            if (takeCount > 0)
                            {
                                }
                        }

                    }
                    catch
                    {
                    }
                }
            }

            string installedFile = Path.Combine(programData, "Epic", "UnrealEngineLauncher", "LauncherInstalled.dat");
            if (!File.Exists(installedFile))
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
                        !Directory.Exists(installLocation))
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
                                        IsExcludedUtilityEntry(displayName, installLocation))
                                    {
                                        continue;
                                    }

                                    string uninstallPath = CleanExecutablePath(
                                        (appKey.GetValue("UninstallString") as string) ??
                                        (appKey.GetValue("QuietUninstallString") as string));
                                    string fallbackDirectory = installLocation;
                                    if ((string.IsNullOrWhiteSpace(fallbackDirectory) || !Directory.Exists(fallbackDirectory)) &&
                                        IsExecutablePath(uninstallPath))
                                    {
                                        fallbackDirectory = Path.GetDirectoryName(uninstallPath);
                                    }

                                    string executablePath = CleanExecutablePath(appKey.GetValue("DisplayIcon") as string);
                                    int metadataScore = GetTextMatchScore(displayName, fallbackDirectory, query);
                                    int executableScore = IsExecutablePath(executablePath)
                                        ? GetTextMatchScore(Path.GetFileNameWithoutExtension(executablePath), executablePath, query)
                                        : -1;
                                    if (metadataScore < 0 && executableScore < 0)
                                    {
                                        continue;
                                    }
                                    if (IsExecutablePath(executablePath) && !IsExcludedUtilityEntry(displayName, executablePath))
                                    {
                                        AddCandidate(candidates, displayName, executablePath, "Installed Programs", false);
                                        continue;
                                    }

                                    if (!string.IsNullOrWhiteSpace(fallbackDirectory) && Directory.Exists(fallbackDirectory))
                                    {
                                        List<SteamExecutableCandidate> executables = FindSteamExecutables(
                                            displayName,
                                            Path.GetFileName(fallbackDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                                            fallbackDirectory);
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

                        int takeCount = Math.Min(1, executables.Count);
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
                "legacy", "benchmark", "webwallpaper", "wallpaperui", "monitor",
                "fixer", "helper", "appex"
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

        private static bool ContainsSearchPhrase(string value, string query)
        {
            if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(query))
            {
                return false;
            }

            bool allowInsideWord = false;
            foreach (char character in query)
            {
                if (character > 127)
                {
                    allowInsideWord = true;
                    break;
                }
            }

            int startIndex = 0;
            while (startIndex < value.Length)
            {
                int matchIndex = value.IndexOf(query, startIndex, StringComparison.Ordinal);
                if (matchIndex < 0)
                {
                    return false;
                }
                if (allowInsideWord || matchIndex == 0 || value[matchIndex - 1] == ' ')
                {
                    return true;
                }
                startIndex = matchIndex + 1;
            }
            return false;
        }

        private static bool ParentNameMatches(string normalizedParent, string normalizedQuery)
        {
            if (normalizedParent.Length == 0 || normalizedQuery.Length == 0)
            {
                return false;
            }
            if (normalizedParent.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                return true;
            }

            string[] tokens = normalizedParent.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string token in tokens)
            {
                if (token.StartsWith(normalizedQuery, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static int GetTextMatchScore(string primary, string secondary, string query)
        {
            string normalizedQuery = NormalizeForSearch(query);
            string normalizedPrimary = NormalizeForSearch(primary);
            string normalizedSecondary = NormalizeForSearch(secondary);
            if (normalizedQuery.Length == 0)
            {
                return 30;
            }
            if (normalizedPrimary == normalizedQuery || normalizedSecondary == normalizedQuery)
            {
                return 100;
            }
            if (normalizedPrimary.StartsWith(normalizedQuery, StringComparison.Ordinal) ||
                normalizedSecondary.StartsWith(normalizedQuery, StringComparison.Ordinal))
            {
                return 80;
            }
            if (ContainsSearchPhrase(normalizedPrimary, normalizedQuery) ||
                ContainsSearchPhrase(normalizedSecondary, normalizedQuery))
            {
                return 60;
            }

            string compactQuery = GetCompactSearchText(normalizedQuery);
            string compactPrimary = GetCompactSearchText(normalizedPrimary);
            string compactSecondary = GetCompactSearchText(normalizedSecondary);
            if (compactQuery.Length > 0 &&
                (compactPrimary == compactQuery || compactSecondary == compactQuery))
            {
                return 90;
            }
            if (compactQuery.Length > 0 &&
                ((compactPrimary.Length > 0 && compactPrimary.StartsWith(compactQuery, StringComparison.Ordinal)) ||
                 (compactSecondary.Length > 0 && compactSecondary.StartsWith(compactQuery, StringComparison.Ordinal))))
            {
                return 75;
            }
            if (compactQuery == GetAcronym(normalizedPrimary) ||
                compactQuery == GetAcronym(normalizedSecondary) ||
                normalizedQuery == GetAcronym(normalizedPrimary) ||
                normalizedQuery == GetAcronym(normalizedSecondary))
            {
                return 65;
            }

            string[] queryTokens = normalizedQuery.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (queryTokens.Length > 1)
            {
                bool allTokensMatch = true;
                foreach (string token in queryTokens)
                {
                    if ((normalizedPrimary.Length > 0 && normalizedPrimary.IndexOf(token, StringComparison.Ordinal) < 0) &&
                        (normalizedSecondary.Length > 0 && normalizedSecondary.IndexOf(token, StringComparison.Ordinal) < 0))
                    {
                        allTokensMatch = false;
                        break;
                    }
                }
                if (allTokensMatch)
                {
                    return 45;
                }
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
        private static SearchResult AddCandidate(
            Dictionary<string, SearchResult> candidates,
            string displayName,
            string executablePath,
            string source,
            bool isRunning,
            bool hasMainWindow = false)
        {
            if (string.IsNullOrWhiteSpace(displayName) || !IsExecutablePath(executablePath))
            {
                return null;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(executablePath);
            }
            catch
            {
                return null;
            }

            string normalizedDisplayName = displayName.Trim();
            SearchResult existing;
            if (candidates.TryGetValue(fullPath, out existing))
            {
                existing.IsRunning = existing.IsRunning || isRunning;
                existing.HasMainWindow = existing.HasMainWindow || hasMainWindow;
                existing.AddAlias(normalizedDisplayName);
                return existing;
            }

            SearchResult result = new SearchResult();
            result.DisplayName = normalizedDisplayName;
            result.ExecutablePath = fullPath;
            result.ProcessName = Path.GetFileName(fullPath);
            result.Source = source;
            result.IsRunning = isRunning;
            result.HasMainWindow = hasMainWindow;
            result.ApplicationKey = GetApplicationKey(normalizedDisplayName, fullPath);
            result.AddAlias(normalizedDisplayName);
            candidates[fullPath] = result;
            return result;
        }

        private static bool HasMatchingCandidate(
            Dictionary<string, SearchResult> candidates,
            string query)
        {
            foreach (SearchResult candidate in candidates.Values)
            {
                if (IsBackgroundSearchResult(candidate) ||
                    IsBuiltInWindowsApplication(candidate))
                {
                    continue;
                }
                if (GetMatchScore(candidate, query) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsInstallerOrMaintenanceApplication(SearchResult result)
        {
            return result != null && GetMaintenanceRiskScore(result) >= 5;
        }

        private static int GetMaintenanceRiskScore(SearchResult result)
        {
            if (result == null)
            {
                return 100;
            }

            string fileName = Path.GetFileNameWithoutExtension(result.ProcessName);
            string path = (result.ExecutablePath ?? string.Empty).ToLowerInvariant();
            int score = 0;

            if (ContainsMaintenanceKeyword(fileName))
            {
                score += 3;
            }
            if (ContainsMaintenanceKeyword(result.DisplayName))
            {
                score += 2;
            }
            if (result.Aliases != null)
            {
                foreach (string alias in result.Aliases)
                {
                    if (ContainsMaintenanceKeyword(alias))
                    {
                        score += 2;
                        break;
                    }
                }
            }

            string[] maintenancePaths = new string[]
            {
                @"\installer\", @"\installers\", @"\uninstall\", @"\updates\",
                @"\updater\", @"\maintenance\", @"\bootstrapper\", @"\squirrel\",
                @"\downloader\", @"\downloaders\", @"\webinstaller\", @"\appinstaller\",
                @"\package cache\", @"\softwaredistribution\"
            };
            foreach (string fragment in maintenancePaths)
            {
                if (path.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    score += 3;
                    break;
                }
            }

            if (path.IndexOf(@"\appdata\local\temp\", StringComparison.Ordinal) >= 0 ||
                path.IndexOf(@"\windows\temp\", StringComparison.Ordinal) >= 0 ||
                path.IndexOf(@"_mei", StringComparison.Ordinal) >= 0)
            {
                score += 2;
            }

            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(result.ExecutablePath);
                if (ContainsMaintenanceKeyword(version.OriginalFilename) ||
                    ContainsMaintenanceKeyword(version.InternalName) ||
                    ContainsMaintenanceKeyword(version.FileDescription) ||
                    ContainsMaintenanceKeyword(version.ProductName) ||
                    ContainsMaintenanceKeyword(version.Comments))
                {
                    score += 3;
                }
            }
            catch
            {
            }

            if (result.HasMainWindow)
            {
                score -= 2;
            }
            if (IsUserEntrySource(result.Source))
            {
                score -= 2;
            }
            return score;
        }

        private static bool IsBackgroundSearchResult(SearchResult result)
        {
            if (result == null)
            {
                return true;
            }

            string fileName = Path.GetFileNameWithoutExtension(result.ProcessName);
            if (string.Equals(fileName, "store", StringComparison.OrdinalIgnoreCase) ||
                IsDefinitelyBackgroundName(fileName) ||
                IsDefinitelyBackgroundName(result.DisplayName) ||
                IsInstallerOrMaintenanceApplication(result) ||
                IsHardBackgroundPath(result.ExecutablePath))
            {
                return true;
            }
            return GetBackgroundRiskScore(result) >= 5;
        }

        private static int GetBackgroundRiskScore(SearchResult result)
        {
            string fileName = Path.GetFileNameWithoutExtension(result.ProcessName);
            string displayName = result.DisplayName;
            int score = 0;

            string[] strongWords = new string[]
            {
                "helper", "service", "host", "broker", "daemon", "overlay",
                "renderer", "webview", "cef", "crash", "report", "telemetry",
                "plugin", "遥测", "日志", "诊断"
            };
            string[] weakWords = new string[]
            {
                "runtime", "launcher", "utility", "agent", "module", "component",
                "sdk", "viewer", "monitor", "provider", "updater", "installer",
                "服务", "宿主", "代理", "插件", "控制中心", "驱动"
            };

            if (ContainsAnyKeyword(fileName, strongWords))
            {
                score += 3;
            }
            else if (ContainsAnyKeyword(fileName, weakWords))
            {
                score += 3;
            }
            if (ContainsAnyKeyword(displayName, strongWords))
            {
                score += 2;
            }
            else if (ContainsAnyKeyword(displayName, weakWords))
            {
                score += 2;
            }

            if (result.Aliases != null)
            {
                foreach (string alias in result.Aliases)
                {
                    if (ContainsAnyKeyword(alias, strongWords))
                    {
                        score += 2;
                        break;
                    }
                }
            }

            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(result.ExecutablePath);
                string metadata = (version.FileDescription ?? string.Empty) + " " +
                    (version.ProductName ?? string.Empty) + " " +
                    (version.InternalName ?? string.Empty);
                if (ContainsAnyKeyword(metadata, strongWords))
                {
                    score += 2;
                }
                else if (ContainsAnyKeyword(metadata, weakWords))
                {
                    score += 1;
                }
            }
            catch
            {
            }

            if (!result.HasMainWindow)
            {
                score += 2;
            }
            else
            {
                score -= 3;
            }
            if (IsUserEntrySource(result.Source))
            {
                score -= 2;
            }
            return score;
        }

        private static bool ContainsAnyKeyword(string value, string[] keywords)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }
            string normalized = value.ToLowerInvariant();
            foreach (string keyword in keywords)
            {
                if (normalized.IndexOf(keyword, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsUserEntrySource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }
            return source.IndexOf("desktop", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("start menu", StringComparison.OrdinalIgnoreCase) >= 0 ||
                source.IndexOf("桌面", StringComparison.Ordinal) >= 0 ||
                source.IndexOf("开始菜单", StringComparison.Ordinal) >= 0;
        }

        private static bool IsDefinitelyBackgroundName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.ToLowerInvariant();
            string[] names = new string[]
            {
                "tabtip", "textinputhost", "applicationframehost", "nahimic",
                "nvcontainer", "frameview", "presentmon", "faceitclient",
                "gamingcenter", "controlcenter", "mycontrolcenter", "uniwill",
                "osdtp", "clicktorun", "wechatappex", "wechatocr", "wechatutility",
                "wechatplayer", "webwallpaper", "wallpaperui", "translucenttb",
                "gamebar", "winget", "packagemanager", "msoasb", "officeplus",
                "flydigi", "msoev", "语言首选项"
            };
            foreach (string name in names)
            {
                if (normalized.IndexOf(name, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsHardBackgroundPath(string executablePath)
        {
            string path = (executablePath ?? string.Empty).ToLowerInvariant();
            string[] fragments = new string[]
            {
                @"\tencent\wechat\xplugin\", @"\program files\oem\",
                @"\windowsapps\a-volute.nahimic_", @"\windowsapps\senarytechnologylimited.",
                @"\windowsapps\appup.intelgraphicsexperience_", @"\windowsapps\controlcenter",
                @"\flydigi\", @"\nahimic\", @"\microsoft\edgewebview\",
                @"\microsoft\edgecore\"
            };
            foreach (string fragment in fragments)
            {
                if (path.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool ContainsMaintenanceKeyword(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.ToLowerInvariant();
            string[] keywords = new string[]
            {
                "install", "setup", "uninstall", "unins", "update", "updater", "upgrade",
                "repair", "patch", "maintenance", "bootstrapper", "bootstrap", "squirrel",
                "msiexec", "wusa", "usoclient", "trustedinstaller", "tiworker",
                "doctor", "selfcheck", "download", "downloader", "webinstaller", "clicktorun",
                "onlineinstaller", "appinstaller", "appinstall", "packageinstaller",
                "installshield", "deployment", "stubinstaller", "setupwizard", "setupassistant",
                "安装", "更新", "卸载", "修复", "升级", "补丁", "维护", "下载",
                "下载器", "安装器", "安装包", "应用安装", "在线安装", "安装程序",
                "更新程序", "卸载程序"
            };
            foreach (string keyword in keywords)
            {
                if (normalized.IndexOf(keyword, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsBuiltInWindowsApplication(SearchResult result)
        {
            if (result == null)
            {
                return true;
            }

            string path = (result.ExecutablePath ?? string.Empty).ToLowerInvariant();
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
            if (path.StartsWith(windowsDirectory + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return true;
            }

            if (path.IndexOf(@"\program files\windowsapps\", StringComparison.Ordinal) >= 0)
            {
                int packageStart = path.IndexOf(@"\program files\windowsapps\", StringComparison.Ordinal) +
                    @"\program files\windowsapps\".Length;
                int packageEnd = path.IndexOf(Path.DirectorySeparatorChar, packageStart);
                string packageName = packageEnd > packageStart
                    ? path.Substring(packageStart, packageEnd - packageStart)
                    : path.Substring(packageStart);
                if (packageName.StartsWith("microsoft.", StringComparison.Ordinal) ||
                    packageName.StartsWith("microsoftcorporationii.", StringComparison.Ordinal) ||
                    packageName.StartsWith("microsoftwindows.", StringComparison.Ordinal) ||
                    packageName.StartsWith("msteams_", StringComparison.Ordinal) ||
                    packageName.StartsWith("clipchamp.", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            if (path.IndexOf(@"\microsoft\edge\", StringComparison.Ordinal) >= 0 ||
                path.IndexOf(@"\microsoft\edgecore\", StringComparison.Ordinal) >= 0 ||
                path.IndexOf(@"\internet explorer\", StringComparison.Ordinal) >= 0 ||
                path.IndexOf(@"\windows mail\", StringComparison.Ordinal) >= 0 ||
                path.IndexOf(@"\windows media player\", StringComparison.Ordinal) >= 0 ||
                path.IndexOf(@"\microsoft\edgewebview\", StringComparison.Ordinal) >= 0 ||
                path.IndexOf(@"\microsoft\edgecore\", StringComparison.Ordinal) >= 0 ||
                path.IndexOf(@"\microsoft\copilot", StringComparison.Ordinal) >= 0)
            {
                return true;
            }

            string fileName = Path.GetFileNameWithoutExtension(result.ProcessName).ToLowerInvariant();
            string[] builtInNames = new string[]
            {
                "notepad", "mspaint", "snippingtool", "regedit", "iexplore",
                "winstore.app", "calculatorapp", "windowscamera", "photos", "soundrec",
                "time", "weatherstub", "maps", "video.ui", "stickynotesstub",
                "familyhub", "xboxpcappce", "wt", "pilotshub", "copilotapp",
                "mscopilot", "msedge", "msedge_proxy", "wab", "wabmig",
                "wmplayer", "wordpad", "tabtip", "textinputhost", "applicationframehost"
            };
            foreach (string builtInName in builtInNames)
            {
                if (string.Equals(fileName, builtInName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddLauncherMainApplications(Dictionary<string, SearchResult> candidates)
        {
            List<SearchResult> existing = new List<SearchResult>(candidates.Values);
            HashSet<string> seenPaths = new HashSet<string>(candidates.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (SearchResult candidate in existing)
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(candidate.ProcessName);
                    string stem = GetLauncherStem(fileName);
                    if (string.IsNullOrWhiteSpace(stem))
                    {
                        continue;
                    }

                    string directory = Path.GetDirectoryName(candidate.ExecutablePath);
                    if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    {
                        continue;
                    }

                    string mainPath = Path.Combine(directory, stem + ".exe");
                    if (IsExecutablePath(mainPath) && seenPaths.Add(mainPath))
                    {
                        SearchResult companion = AddCandidate(
                            candidates,
                            candidate.DisplayName,
                            mainPath,
                            candidate.Source,
                            candidate.IsRunning,
                            false);
                        if (companion != null)
                        {
                            companion.ApplicationKey = candidate.ApplicationKey;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        private static string GetLauncherStem(string fileName)
        {
            string value = (fileName ?? string.Empty).Trim();
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
                    return value.Substring(0, value.Length - suffix.Length).Trim(' ', '_', '-', '.');
                }
            }
            return null;
        }

        private static SearchResult AddProcessOnlyCandidate(
            Dictionary<string, SearchResult> candidates,
            string displayName,
            string processName,
            string source,
            bool hasMainWindow)
        {
            if (string.IsNullOrWhiteSpace(processName))
            {
                return null;
            }

            string normalizedProcessName = AppSettings.NormalizeProcessName(processName);
            if (normalizedProcessName == null)
            {
                return null;
            }

            string key = "process:" + normalizedProcessName.ToLowerInvariant();
            SearchResult existing;
            if (candidates.TryGetValue(key, out existing))
            {
                existing.IsRunning = true;
                existing.HasMainWindow = existing.HasMainWindow || hasMainWindow;
                existing.AddAlias(displayName);
                existing.AddAlias(Path.GetFileNameWithoutExtension(normalizedProcessName));
                return existing;
            }

            SearchResult result = new SearchResult();
            result.DisplayName = string.IsNullOrWhiteSpace(displayName)
                ? Path.GetFileNameWithoutExtension(normalizedProcessName)
                : displayName.Trim();
            result.ExecutablePath = normalizedProcessName;
            result.ProcessName = normalizedProcessName;
            result.Source = source;
            result.IsRunning = true;
            result.HasMainWindow = hasMainWindow;
            result.ApplicationKey = key;
            result.AddAlias(result.DisplayName);
            result.AddAlias(Path.GetFileNameWithoutExtension(normalizedProcessName));
            candidates[key] = result;
            return result;
        }

        private static string GetApplicationKey(string displayName, string executablePath)
        {
            try
            {
                FileVersionInfo version = FileVersionInfo.GetVersionInfo(executablePath);
                string product = NormalizeForSearch(version.ProductName);
                string company = NormalizeForSearch(version.CompanyName);
                if (product.Length > 0 && !IsGenericApplicationProduct(product))
                {
                    return "product:" + product + "|" + company;
                }
            }
            catch
            {
            }

            string normalizedName = NormalizeForSearch(Path.GetFileNameWithoutExtension(executablePath));
            if (normalizedName.Length == 0)
            {
                normalizedName = NormalizeForSearch(displayName);
            }
            return "file:" + normalizedName;
        }

        private static bool IsGenericApplicationProduct(string product)
        {
            string value = (product ?? string.Empty).ToLowerInvariant();
            string[] genericNames = new string[]
            {
                "application", "app", "electron", "chromium", "runtime", "launcher",
                "microsoft windows", "windows", "setup", "installer", "node.js", "python",
                "microsoft visual c++", "microsoft .net"
            };
            foreach (string name in genericNames)
            {
                if (string.Equals(value, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static List<SearchResult> CollapseDuplicateApplications(
            List<SearchResult> matches,
            SoftwareSearchScope scope)
        {
            Dictionary<string, SearchResult> collapsed =
                new Dictionary<string, SearchResult>(StringComparer.OrdinalIgnoreCase);
            foreach (SearchResult candidate in matches)
            {
                string key = candidate.ApplicationKey;
                if (string.IsNullOrWhiteSpace(key))
                {
                    key = GetApplicationKey(candidate.DisplayName, candidate.ExecutablePath);
                }

                SearchResult existing;
                if (!collapsed.TryGetValue(key, out existing))
                {
                    collapsed[key] = candidate;
                    continue;
                }

                MergeSearchResultMetadata(candidate, existing);
                SearchResult preferred = GetApplicationPreferenceScore(candidate) > GetApplicationPreferenceScore(existing)
                    ? candidate
                    : existing;
                SearchResult other = preferred == candidate ? existing : candidate;
                MergeSearchResultMetadata(preferred, other);
                collapsed[key] = preferred;
            }
            return new List<SearchResult>(collapsed.Values);
        }

        private static void MergeSearchResultMetadata(SearchResult target, SearchResult source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.IsRunning = target.IsRunning || source.IsRunning;
            target.HasMainWindow = target.HasMainWindow || source.HasMainWindow;
            target.Score = Math.Max(target.Score, source.Score);
            if (source.Aliases != null)
            {
                foreach (string alias in source.Aliases)
                {
                    target.AddAlias(alias);
                }
            }
        }

        private static int GetApplicationPreferenceScore(SearchResult result)
        {
            if (result == null)
            {
                return -1;
            }

            int score = result.Score * 10;
            if (result.HasMainWindow)
            {
                score += 10000;
            }
            if (result.IsRunning)
            {
                score += 500;
            }

            string fileName = Path.GetFileNameWithoutExtension(result.ProcessName);
            if (HasExcludedExecutableName(fileName))
            {
                score -= 2000;
            }
            score -= GetPathDepth(result.ExecutablePath) * 5;
            score -= fileName.Length;
            return score;
        }

        private static int GetPathDepth(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return 0;
            }

            int depth = 0;
            foreach (char character in path)
            {
                if (character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar)
                {
                    depth++;
                }
            }
            return depth;
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
            if (result.Aliases != null)
            {
                foreach (string alias in result.Aliases)
                {
                    score = Math.Max(score, GetTextMatchScore(alias, fileName, query));
                }
            }

            if (score < 0)
            {
                string normalizedQuery = NormalizeForSearch(query);
                string parentDirectory = null;
                try
                {
                    parentDirectory = Path.GetDirectoryName(result.ExecutablePath);
                }
                catch
                {
                }

                string normalizedParent = NormalizeForSearch(
                    string.IsNullOrWhiteSpace(parentDirectory)
                        ? string.Empty
                        : Path.GetFileName(parentDirectory));
                if (normalizedQuery.Length > 0 && ParentNameMatches(normalizedParent, normalizedQuery))
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

        private static void EnsureFullDriveIndexStarted()
        {
            lock (FullDriveIndexLock)
            {
                if (_fullDriveIndexStarted)
                {
                    return;
                }
                _fullDriveIndexStarted = true;
            }

            System.Threading.Thread worker = new System.Threading.Thread(FullDriveIndexWorker);
            worker.IsBackground = true;
            worker.Name = "TokeIME full-drive index";
            worker.Start();
        }

        private static bool IsFullDriveIndexCompleted()
        {
            lock (FullDriveIndexLock)
            {
                return _fullDriveIndexCompleted;
            }
        }

        private static string GetFullDriveIndexStatusText()
        {
            lock (FullDriveIndexLock)
            {
                if (!_fullDriveIndexStarted)
                {
                    return string.Empty;
                }
                if (!_fullDriveIndexCompleted)
                {
                    return "Scanning all drives in the background. Results will refresh automatically.";
                }
                if (!string.IsNullOrWhiteSpace(_fullDriveIndexStatus))
                {
                    return "Full-drive index incomplete: " + _fullDriveIndexStatus;
                }
                int count = _fullDriveIndex == null ? 0 : _fullDriveIndex.Count;
                return "Full-drive index complete: " + count + " launchable applications.";
            }
        }

        private static List<SearchResult> GetFullDriveIndexSnapshot()
        {
            lock (FullDriveIndexLock)
            {
                return _fullDriveIndex;
            }
        }

        private static void FullDriveIndexWorker()
        {
            List<SearchResult> index = null;
            string error = string.Empty;
            try
            {
                index = BuildFullDriveApplicationIndex();
            }
            catch (Exception exception)
            {
                error = exception.Message;
            }

            lock (FullDriveIndexLock)
            {
                _fullDriveIndex = index;
                _fullDriveIndexStatus = error;
                _fullDriveIndexCompleted = true;
            }
        }

        private static void AddFullDriveApplications(Dictionary<string, SearchResult> candidates, string query)
        {
            List<SearchResult> index = GetFullDriveIndexSnapshot();
            if (index == null)
            {
                return;
            }

            foreach (SearchResult indexedResult in index)
            {
                if (GetMatchScore(indexedResult, query) >= 0)
                {
                    AddIndexedCandidate(candidates, indexedResult);
                }
            }
        }

        private static void AddIndexedCandidate(Dictionary<string, SearchResult> candidates, SearchResult indexedResult)
        {
            if (indexedResult == null || string.IsNullOrWhiteSpace(indexedResult.ExecutablePath))
            {
                return;
            }

            SearchResult existing;
            if (candidates.TryGetValue(indexedResult.ExecutablePath, out existing))
            {
                existing.IsRunning = existing.IsRunning || indexedResult.IsRunning;
                if (indexedResult.Aliases != null)
                {
                    foreach (string alias in indexedResult.Aliases)
                    {
                        existing.AddAlias(alias);
                    }
                }
                return;
            }

            SearchResult copy = new SearchResult();
            copy.DisplayName = indexedResult.DisplayName;
            copy.ExecutablePath = indexedResult.ExecutablePath;
            copy.ProcessName = indexedResult.ProcessName;
            copy.Source = indexedResult.Source;
            copy.IsRunning = indexedResult.IsRunning;
            if (indexedResult.Aliases != null)
            {
                foreach (string alias in indexedResult.Aliases)
                {
                    copy.AddAlias(alias);
                }
            }
            candidates[copy.ExecutablePath] = copy;
        }

        private static void TryResolveShortcutFromIndex(
            Dictionary<string, SearchResult> candidates,
            string shortcutPath,
            string source)
        {
            string shortcutName = Path.GetFileNameWithoutExtension(shortcutPath);
            if (string.IsNullOrWhiteSpace(shortcutName))
            {
                return;
            }

            List<SearchResult> storeApplications = GetWindowsStoreApplications();
            SearchResult bestStoreApplication = null;
            int bestStoreScore = -1;
            foreach (SearchResult storeApplication in storeApplications)
            {
                int score = GetMatchScore(storeApplication, shortcutName);
                if (score > bestStoreScore)
                {
                    bestStoreApplication = storeApplication;
                    bestStoreScore = score;
                }
            }
            if (bestStoreApplication != null && bestStoreScore >= 30)
            {
                AddIndexedCandidate(candidates, bestStoreApplication);
                AddCandidate(candidates, shortcutName, bestStoreApplication.ExecutablePath, source, false);
                return;
            }

            string aliasDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft",
                "WindowsApps");
            string aliasPath = Path.Combine(aliasDirectory, shortcutName + ".exe");
            if (IsExecutablePath(aliasPath))
            {
                AddCandidate(candidates, shortcutName, aliasPath, source, false);
                return;
            }

            List<SearchResult> index = GetFullDriveIndexSnapshot();
            if (index == null)
            {
                return;
            }

            SearchResult best = null;
            int bestScore = -1;
            foreach (SearchResult indexedResult in index)
            {
                int score = GetMatchScore(indexedResult, shortcutName);
                if (score > bestScore ||
                    (score == bestScore && best != null &&
                     indexedResult.ExecutablePath.Length < best.ExecutablePath.Length))
                {
                    best = indexedResult;
                    bestScore = score;
                }
            }

            if (best == null || bestScore < 30)
            {
                return;
            }

            AddIndexedCandidate(candidates, best);
            AddCandidate(candidates, shortcutName, best.ExecutablePath, source, false);
        }

        private static string GetCompactSearchText(string normalizedValue)
        {
            if (string.IsNullOrEmpty(normalizedValue))
            {
                return string.Empty;
            }
            return normalizedValue.Replace(" ", string.Empty);
        }

        private static IEnumerable<string> EnumerateFilesSafely(string root)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] files;
                string[] subdirectories;
                try
                {
                    files = Directory.GetFiles(directory);
                    subdirectories = Directory.GetDirectories(directory);
                }
                catch
                {
                    continue;
                }

                foreach (string file in files)
                {
                    yield return file;
                }
                foreach (string subdirectory in subdirectories)
                {
                    try
                    {
                        if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(subdirectory);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void AddUrlShortcutCandidate(
            Dictionary<string, SearchResult> candidates,
            string shortcutPath,
            string source)
        {
            try
            {
                string url = ReadShortcutUrl(shortcutPath);
                if (string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                Match steamMatch = Regex.Match(
                    url.Trim(),
                    @"^steam://rungameid/([0-9]+)",
                    RegexOptions.IgnoreCase);
                if (!steamMatch.Success)
                {
                    return;
                }

                string appId = steamMatch.Groups[1].Value;
                string gameName;
                string gameDirectory;
                if (!TryGetSteamApplication(appId, out gameName, out gameDirectory))
                {
                    return;
                }

                List<SteamExecutableCandidate> executables = FindSteamExecutables(
                    gameName,
                    Path.GetFileName(gameDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    gameDirectory);
                if (executables.Count == 0)
                {
                    return;
                }

                string executablePath = executables[0].ExecutablePath;
                string shortcutName = Path.GetFileNameWithoutExtension(shortcutPath);
                AddCandidate(candidates, gameName, executablePath, source, false);
                AddCandidate(candidates, shortcutName, executablePath, source, false);
            }
            catch
            {
            }
        }

        private static string ReadShortcutUrl(string shortcutPath)
        {
            foreach (string line in File.ReadAllLines(shortcutPath))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    return line.Substring(4).Trim();
                }
            }
            return null;
        }

        private static bool TryGetSteamApplication(string appId, out string gameName, out string gameDirectory)
        {
            gameName = null;
            gameDirectory = null;
            string manifestName = "appmanifest_" + appId + ".acf";
            foreach (string libraryRoot in FindSteamLibraryRoots())
            {
                try
                {
                    string steamApps = Path.Combine(libraryRoot, "steamapps");
                    string manifestPath = Path.Combine(steamApps, manifestName);
                    if (!File.Exists(manifestPath))
                    {
                        continue;
                    }

                    string manifest = File.ReadAllText(manifestPath);
                    string installDirectory = ReadVdfValue(manifest, "installdir");
                    string name = ReadVdfValue(manifest, "name");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirectory))
                    {
                        continue;
                    }

                    string candidateDirectory = Path.Combine(steamApps, "common", installDirectory);
                    if (Directory.Exists(candidateDirectory))
                    {
                        gameName = name;
                        gameDirectory = candidateDirectory;
                        return true;
                    }
                }
                catch
                {
                }
            }
            return false;
        }

        private static bool IsExcludedBackgroundProcessPath(string executablePath)
        {
            string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (IsPathWithinDirectory(executablePath, windowsDirectory))
            {
                return true;
            }

            string[] programDirectories = new string[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            foreach (string programDirectory in programDirectories)
            {
                if (!string.IsNullOrWhiteSpace(programDirectory) &&
                    IsPathWithinDirectory(executablePath, Path.Combine(programDirectory, "WindowsApps")))
                {
                    return true;
                }
            }

            string normalizedPath = executablePath.ToLowerInvariant();
            string[] excludedFragments = new string[]
            {
                @"\openai\codex\bin\", @"\openai\codex\runtimes\",
                @"\.cache\codex-runtimes\", @"\node_modules\", @"\appdata\local\temp\"
            };
            foreach (string fragment in excludedFragments)
            {
                if (normalizedPath.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static bool IsPathWithinDirectory(string path, string directory)
        {
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            try
            {
                string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(fullPath, fullDirectory, StringComparison.OrdinalIgnoreCase) ||
                    fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void AddWindowsStoreApplications(Dictionary<string, SearchResult> candidates, string query)
        {
            List<SearchResult> applications = GetWindowsStoreApplications();
            foreach (SearchResult application in applications)
            {
                if (GetMatchScore(application, query) >= 0)
                {
                    AddIndexedCandidate(candidates, application);
                }
            }
        }

        private static List<SearchResult> GetWindowsStoreApplications()
        {
            lock (WindowsStoreIndexLock)
            {
                if (_windowsStoreApplications != null)
                {
                    return _windowsStoreApplications;
                }
            }

            List<SearchResult> applications = BuildWindowsStoreApplications();
            lock (WindowsStoreIndexLock)
            {
                _windowsStoreApplications = applications;
                return _windowsStoreApplications;
            }
        }

        private static List<SearchResult> BuildWindowsStoreApplications()
        {
            List<SearchResult> results = new List<SearchResult>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            object shell = null;
            object folder = null;
            object items = null;
            try
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    return results;
                }

                shell = Activator.CreateInstance(shellType);
                folder = shellType.InvokeMember(
                    "Namespace",
                    BindingFlags.InvokeMethod,
                    null,
                    shell,
                    new object[] { "shell:AppsFolder" });
                if (folder == null)
                {
                    return results;
                }

                items = folder.GetType().InvokeMember(
                    "Items",
                    BindingFlags.InvokeMethod,
                    null,
                    folder,
                    null);
                if (items == null)
                {
                    return results;
                }

                System.Collections.IEnumerable enumerable = items as System.Collections.IEnumerable;
                if (enumerable == null)
                {
                    return results;
                }

                foreach (object item in enumerable)
                {
                    try
                    {
                        string itemName = item.GetType().InvokeMember(
                            "Name",
                            BindingFlags.GetProperty,
                            null,
                            item,
                            null) as string;
                        string appUserModelId = item.GetType().InvokeMember(
                            "Path",
                            BindingFlags.GetProperty,
                            null,
                            item,
                            null) as string;
                        string packageFullName = item.GetType().InvokeMember(
                            "ExtendedProperty",
                            BindingFlags.InvokeMethod,
                            null,
                            item,
                            new object[] { "System.AppUserModel.PackageFullName" }) as string;
                        if (string.IsNullOrWhiteSpace(packageFullName))
                        {
                            continue;
                        }

                        SearchResult application = TryCreateWindowsStoreApplication(
                            itemName,
                            appUserModelId,
                            packageFullName);
                        if (application != null && seenPaths.Add(application.ExecutablePath))
                        {
                            results.Add(application);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            finally
            {
                ReleaseComObject(items);
                ReleaseComObject(folder);
                ReleaseComObject(shell);
            }
            return results;
        }

        private static SearchResult TryCreateWindowsStoreApplication(
            string itemName,
            string appUserModelId,
            string packageFullName)
        {
            try
            {
                string windowsApps = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "WindowsApps");
                string packageDirectory = Path.Combine(windowsApps, packageFullName);
                string manifestPath = Path.Combine(packageDirectory, "AppxManifest.xml");
                if (!File.Exists(manifestPath))
                {
                    return null;
                }

                XmlDocument manifest = new XmlDocument();
                manifest.Load(manifestPath);
                XmlNodeList applications = manifest.SelectNodes("//*[local-name()='Application']");
                if (applications == null)
                {
                    return null;
                }

                string applicationId = string.Empty;
                if (!string.IsNullOrWhiteSpace(appUserModelId))
                {
                    int separator = appUserModelId.LastIndexOf('!');
                    if (separator >= 0 && separator + 1 < appUserModelId.Length)
                    {
                        applicationId = appUserModelId.Substring(separator + 1);
                    }
                }

                foreach (XmlNode applicationNode in applications)
                {
                    try
                    {
                        string id = applicationNode.Attributes == null ? null : applicationNode.Attributes["Id"].Value;
                        if (applicationId.Length > 0 && !string.Equals(id, applicationId, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        XmlElement application = applicationNode as XmlElement;
                        if (application == null)
                        {
                            continue;
                        }

                        string executable = application.GetAttribute("Executable");
                        if (string.IsNullOrWhiteSpace(executable))
                        {
                            continue;
                        }

                        XmlNode visualElement = application.SelectSingleNode(".//*[local-name()='VisualElements']");
                        if (visualElement != null && visualElement.Attributes != null)
                        {
                            XmlAttribute appListEntry = visualElement.Attributes["AppListEntry"];
                            if (appListEntry != null && string.Equals(appListEntry.Value, "none", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }
                        }

                        string executablePath = Path.Combine(packageDirectory, executable.Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(executablePath))
                        {
                            continue;
                        }

                        if (HasExcludedExecutableName(Path.GetFileNameWithoutExtension(executablePath)))
                        {
                            continue;
                        }

                        string displayName = itemName;
                        if (visualElement != null && visualElement.Attributes != null)
                        {
                            XmlAttribute displayNameAttribute = visualElement.Attributes["DisplayName"];
                            if (displayNameAttribute != null &&
                                !string.IsNullOrWhiteSpace(displayNameAttribute.Value) &&
                                !displayNameAttribute.Value.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase))
                            {
                                displayName = displayNameAttribute.Value;
                            }
                        }

                        string finalDisplayName = string.IsNullOrWhiteSpace(displayName)
                            ? Path.GetFileNameWithoutExtension(executablePath)
                            : displayName.Trim();
                        if (HasExcludedExecutableName(finalDisplayName))
                        {
                            continue;
                        }

                        SearchResult result = new SearchResult();
                        result.DisplayName = finalDisplayName;
                        result.ExecutablePath = executablePath;
                        result.ProcessName = Path.GetFileName(executablePath);
                        result.Source = "Windows Apps";
                        result.IsRunning = false;
                        result.AddAlias(itemName);
                        result.AddAlias(appUserModelId);
                        result.AddAlias(packageFullName);
                        result.AddAlias(Path.GetFileNameWithoutExtension(executablePath));
                        return result;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static List<SearchResult> BuildFullDriveApplicationIndex()
        {
            List<SearchResult> results = new List<SearchResult>();
            HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SearchResult storeApplication in GetWindowsStoreApplications())
            {
                if (seenPaths.Add(storeApplication.ExecutablePath))
                {
                    results.Add(storeApplication);
                }
            }
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady)
                    {
                        continue;
                    }

                    foreach (string executablePath in EnumerateIndexedExecutablesSafely(drive.RootDirectory.FullName))
                    {
                        string fullPath;
                        try
                        {
                            fullPath = Path.GetFullPath(executablePath);
                        }
                        catch
                        {
                            continue;
                        }

                        if (!seenPaths.Add(fullPath))
                        {
                            continue;
                        }

                        SearchResult result = TryCreateIndexedApplication(fullPath);
                        if (result != null)
                        {
                            results.Add(result);
                        }
                    }
                }
                catch
                {
                }
            }

            results.Sort(delegate(SearchResult left, SearchResult right)
            {
                return string.Compare(left.DisplayName, right.DisplayName, StringComparison.CurrentCultureIgnoreCase);
            });
            return results;
        }

        private static IEnumerable<string> EnumerateIndexedExecutablesSafely(string root)
        {
            Stack<string> pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string directory = pending.Pop();
                string[] files;
                string[] subdirectories;
                try
                {
                    files = Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly);
                    subdirectories = Directory.GetDirectories(directory);
                }
                catch
                {
                    continue;
                }

                foreach (string file in files)
                {
                    yield return file;
                }

                foreach (string subdirectory in subdirectories)
                {
                    if (IsExcludedIndexDirectory(subdirectory))
                    {
                        continue;
                    }

                    try
                    {
                        if ((File.GetAttributes(subdirectory) & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(subdirectory);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static bool IsExcludedIndexDirectory(string directory)
        {
            string name = Path.GetFileName(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToLowerInvariant();
            string fullPath = directory.ToLowerInvariant();
            string[] excludedNames = new string[]
            {
                "$recycle.bin", "system volume information", "$windows.~bt", "$windows.~ws",
                "windows", "winsxs", "servicing", "driverstore", "node_modules", ".git", ".svn",
                ".cache", ".nuget", ".gradle", ".cargo", "obj", "packages", "temp", "tmp",
                "cache", "caches", "crashdumps", "installer", "installers", "uninstall",
                "updates", "updater", "maintenance", "bootstrapper", "redist",
                "redistributables", "webcache", "shadercache", "downloading",
                "downloader", "downloaders", "webinstaller", "appinstaller", "logs"
            };
            foreach (string excludedName in excludedNames)
            {
                if (string.Equals(name, excludedName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            string[] excludedPathFragments = new string[]
            {
                @"\programdata\package cache\", @"\microsoft\windows defender\",
                @"\microsoft\visualstudio\packages\", @"\nvidia corporation\downloader\",
                @"\nvidia app\updateframework\", @"\openai\codex\bin\",
                @"\openai\codex\runtimes\", @"\.codex\", @"\steamapps\workshop\",
                @"\tencent\wechat\xplugin\", @"\uniwillservice\",
                @"\program files\oem\", @"\leigod_acc\resources\",
                @"\resources\client_tools\", @"\resources\tools\",
                @"\windowsapps\a-volute.nahimic_", @"\windowsapps\senarytechnologylimited.",
                @"\windowsapps\appup.intelgraphicsexperience_", @"\windowsapps\controlcenter",
                @"\flydigi\", @"\nahimic\", @"\microsoft\edgewebview\",
                @"\microsoft\edgecore\"
            };
            foreach (string fragment in excludedPathFragments)
            {
                if (fullPath.IndexOf(fragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static SearchResult TryCreateIndexedApplication(string executablePath)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(executablePath);
                if (HasExcludedExecutableName(fileName))
                {
                    return null;
                }

                FileInfo file = new FileInfo(executablePath);
                if (!file.Exists || file.Length < 32 * 1024 || !IsWindowsGuiExecutable(executablePath))
                {
                    return null;
                }

                FileVersionInfo version = FileVersionInfo.GetVersionInfo(executablePath);
                string description = (version.FileDescription ?? string.Empty).Trim();
                string product = (version.ProductName ?? string.Empty).Trim();
                if (ContainsMaintenanceKeyword(fileName) ||
                    ContainsMaintenanceKeyword(version.OriginalFilename) ||
                    ContainsMaintenanceKeyword(version.InternalName) ||
                    ContainsMaintenanceKeyword(description) ||
                    ContainsMaintenanceKeyword(product))
                {
                    return null;
                }
                if (description.Length == 0 && product.Length == 0 && file.Length < 1024 * 1024)
                {
                    return null;
                }

                string displayName = description.Length > 0 ? description : product;
                if (displayName.Length == 0)
                {
                    displayName = fileName;
                }
                if (HasExcludedExecutableName(displayName))
                {
                    return null;
                }

                SearchResult result = new SearchResult();
                result.DisplayName = displayName;
                result.ExecutablePath = executablePath;
                result.ProcessName = Path.GetFileName(executablePath);
                result.Source = "Full-drive index";
                result.IsRunning = false;
                result.AddAlias(description);
                result.AddAlias(product);
                result.AddAlias(fileName);

                try
                {
                    string parentName = Path.GetFileName(Path.GetDirectoryName(executablePath));
                    result.AddAlias(parentName);
                }
                catch
                {
                }
                return result;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsWindowsGuiExecutable(string executablePath)
        {
            try
            {
                using (FileStream stream = new FileStream(
                    executablePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    if (stream.Length < 256)
                    {
                        return false;
                    }

                    stream.Position = 0x3C;
                    int peOffset = reader.ReadInt32();
                    if (peOffset < 0 || peOffset > stream.Length - 128)
                    {
                        return false;
                    }

                    stream.Position = peOffset;
                    uint signature = reader.ReadUInt32();
                    if (signature != 0x00004550)
                    {
                        return false;
                    }

                    int optionalHeaderOffset = peOffset + 24;
                    stream.Position = optionalHeaderOffset;
                    ushort magic = reader.ReadUInt16();
                    if (magic != 0x010B && magic != 0x020B)
                    {
                        return false;
                    }

                    stream.Position = optionalHeaderOffset + 68;
                    ushort subsystem = reader.ReadUInt16();
                    return subsystem == 2;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool HasExcludedExecutableName(string fileName)
        {
            string value = (fileName ?? string.Empty).ToLowerInvariant();
            if (value.Length == 0)
            {
                return true;
            }

            string[] blockedFragments = new string[]
            {
                "unins", "uninstall", "setup", "installer", "update", "updater", "upgrade",
                "crash", "report", "diagnostic", "telemetry", "feedback", "service", "daemon",
                "helper", "broker", "overlay", "crashpad", "webhelper", "notification",
                "systray", "repair", "redist", "vcredist", "dxsetup", "oalinst", "msiexec",
                "rundll", "regsvr", "cmd", "powershell", "wscript", "cscript", "node",
                "nvcontainer", "nahimic", "gameinput", "smartscreen", "host", "runtime",
                "monitor", "fixer", "provider", "smartmax", "command-runner", "commandrunner",
                "launcher", "updateframework", "pwsh", "codex", "conhost", "appex",
                "utility", "ocr", "renderer", "webview", "msedgewebview2", "cef",
                "qtwebengine", "subprocess", "background", "browser", "webwallpaper",
                "wallpaperui", "osdtp", "edgewallpaper", "wechatplayer",
                "anti cheat", "anticheat", "plugin", "clicktorun", "click-to-run",
                "tabtip", "textinputhost", "inputpersonalization", "touchkeyboard", "osk",
                "gamingcenter", "controlcenter", "mycontrolcenter", "uniwill", "mechrevo",
                "senary", "audio console", "nvcplui", "nvidia control panel",
                "igcc", "flydigi", "translucenttb", "gamebar", "winget", "pilotshub",
                "whatsnew", "quickassist", "gethelp", "packagemanager",
                "windows packagemanager", "faceitclient", "frameview", "presentmon",
                "msoasb", "officeplus", "语言首选项", "tool_", "遥测", "日志", "诊断",
                "崩溃", "服务", "更新", "安装", "卸载", "插件", "反作弊",
                "控制中心", "驱动", "downloader", "webinstaller", "onlineinstaller",
                "appinstaller", "packageinstaller"
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
            public SearchResult()
            {
                Aliases = new List<string>();
            }

            public string DisplayName { get; set; }
            public string ExecutablePath { get; set; }
            public string ProcessName { get; set; }
            public string Source { get; set; }
            public bool IsRunning { get; set; }
            public bool HasMainWindow { get; set; }
            public string ApplicationKey { get; set; }
            public int Score { get; set; }
            public List<string> Aliases { get; private set; }

            public void AddAlias(string alias)
            {
                if (string.IsNullOrWhiteSpace(alias))
                {
                    return;
                }

                string trimmed = alias.Trim();
                if (Aliases == null)
                {
                    Aliases = new List<string>();
                }
                if (!Aliases.Exists(delegate(string existing) { return string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase); }))
                {
                    Aliases.Add(trimmed);
                }
            }
        }

        private sealed class SteamExecutableCandidate
        {
            public string ExecutablePath { get; set; }
            public int Score { get; set; }
            public long Length { get; set; }
        }
    }
}
