using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace TokeIME
{
    internal sealed class TrayApplication : ApplicationContext
    {
        internal const string ShowSettingsEventName = @"Local\TokeIME.ShowSettings";

        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _statusItem;

        private readonly ToolStripMenuItem _effectEnabledItem;
        private readonly KeyboardLock _keyboardLock;
        private readonly Timer _foregroundTimer;
        private readonly Icon _inactiveIcon;
        private readonly Icon _activeIcon;
        private readonly bool _ownsInactiveIcon;
        private readonly bool _ownsActiveIcon;

        private readonly HashSet<string> _autoAddSeenProcesses;
        private readonly System.Threading.EventWaitHandle _showSettingsEvent;
        private AppSettings _settings;
        private AutoAddPromptForm _autoAddPrompt;
        private SettingsForm _settingsForm;
        private bool _openSettingsOnFirstTick;
        private bool _manualLocked;
        private string _matchedProcess;
        private bool _settingsOpen;
        private bool _menuOpen;
        private bool _disposed;

        public TrayApplication(bool openSettingsOnStart)
        {
            _settings = AppSettings.Load();
            _openSettingsOnFirstTick = openSettingsOnStart;
            _showSettingsEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, ShowSettingsEventName);
            _autoAddSeenProcesses = CreateVisibleProcessBaseline();
            _keyboardLock = new KeyboardLock();
            _keyboardLock.UpdateOptions(_settings);
            _keyboardLock.ToggleRequested += ToggleTokeEffect;

            bool ownsInactiveIcon;
            bool ownsActiveIcon;
            _inactiveIcon = LoadIconResource("TokeIME.TrayIcon.ico", out ownsInactiveIcon);
            _activeIcon = LoadIconResource("TokeIME.TrayIconActive.ico", out ownsActiveIcon);
            _ownsInactiveIcon = ownsInactiveIcon;
            _ownsActiveIcon = ownsActiveIcon;

            _menu = new ContextMenuStrip();
            _statusItem = new ToolStripMenuItem("Status: Unlocked");
            _statusItem.Enabled = false;

            _effectEnabledItem = new ToolStripMenuItem("Enable TokeIME");
            _effectEnabledItem.Checked = _settings.AutoLockEnabled;
            _effectEnabledItem.Click += AutoLockItemClick;

            ToolStripMenuItem settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += delegate { ShowSettings(); };

            ToolStripMenuItem openConfigItem = new ToolStripMenuItem("Open Config File");
            openConfigItem.Click += delegate { OpenConfigFile(); };

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += delegate { ExitApplication(); };

            _menu.Items.Add(_statusItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_effectEnabledItem);
            _menu.Items.Add(settingsItem);
            _menu.Items.Add(openConfigItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(exitItem);
            _menu.Opening += delegate
            {
                _menuOpen = true;
                UpdateMenuState();
            };
            _menu.Closed += delegate { _menuOpen = false; };

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _inactiveIcon;
            _notifyIcon.Text = "TokeIME - Unlocked";
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += delegate { ShowSettings(); };
            _notifyIcon.BalloonTipClicked += delegate { ShowSettings(); };

            _foregroundTimer = new Timer();
            _foregroundTimer.Interval = 400;
            _foregroundTimer.Tick += ForegroundTimerTick;

            try
            {
                StartupManager.SetEnabled(_settings.StartWithWindows);
            }
            catch
            {
                // A missing startup entry is not fatal for the current session.
            }

            _keyboardLock.Install();
            _foregroundTimer.Start();
            RefreshLockState();
        }

        private void ForegroundTimerTick(object sender, EventArgs e)
        {
            if (_showSettingsEvent.WaitOne(0) || _openSettingsOnFirstTick)
            {
                _openSettingsOnFirstTick = false;
                ShowSettings();
                return;
            }

            if (_menuOpen)
            {
                return;
            }

            string foregroundProcess = NativeMethods.GetForegroundProcessName();
            string nextMatch = null;

            if (_settings.AutoLockEnabled && _settings.IsConfiguredProcess(foregroundProcess))
            {
                nextMatch = foregroundProcess;
            }

            _matchedProcess = nextMatch;
            RefreshLockState();
            CheckAutoAdd(foregroundProcess);
        }

        private void CheckAutoAdd(string foregroundProcess)
        {
            if (!_settings.AutoAddPromptEnabled)
            {
                if (_autoAddPrompt != null && !_autoAddPrompt.IsDisposed)
                {
                    _autoAddPrompt.Close();
                }
                return;
            }

            if (string.IsNullOrEmpty(foregroundProcess) ||
                _settings.IsConfiguredProcess(foregroundProcess) ||
                IsAutoAddExcludedProcess(foregroundProcess))
            {
                return;
            }

            if (!_autoAddSeenProcesses.Add(foregroundProcess))
            {
                return;
            }

            string executablePath = TryGetProcessPath(foregroundProcess);
            ShowAutoAddPrompt(foregroundProcess, executablePath);
        }

        private void ShowAutoAddPrompt(string processName, string executablePath)
        {
            if (_autoAddPrompt != null && !_autoAddPrompt.IsDisposed)
            {
                _autoAddPrompt.Close();
            }

            AutoAddPromptForm prompt = new AutoAddPromptForm(processName, executablePath);
            prompt.Accepted += delegate { AddAutoDetectedProgram(processName); };
            prompt.FormClosed += delegate
            {
                if (_autoAddPrompt == prompt)
                {
                    _autoAddPrompt = null;
                }
            };
            _autoAddPrompt = prompt;
            prompt.Show();
        }

        private void AddAutoDetectedProgram(string processName)
        {
            string normalized = AppSettings.NormalizeProcessName(processName);
            if (normalized == null || _settings.IsConfiguredProcess(normalized))
            {
                return;
            }

            _settings.Processes.Add(normalized);
            try
            {
                _settings.Save();
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                {
                    _settingsForm.AddDetectedProgram(normalized);
                }
                RefreshLockState();
                ToastForm.ShowFor(_settingsForm, "Added", Color.FromArgb(22, 163, 74));
            }
            catch (Exception exception)
            {
                _settings.Processes.Remove(normalized);
                MessageBox.Show(
                    "Failed to add program automatically: \r\n" + exception.Message,
                    "TokeIME",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private static HashSet<string> CreateVisibleProcessBaseline()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        names.Add(process.ProcessName + ".exe");
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return names;
        }

        private static bool IsAutoAddExcludedProcess(string processName)
        {
            string[] excluded = new string[]
            {
                "tokeime.exe",
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

        private static string TryGetProcessPath(string processName)
        {
            string baseName = Path.GetFileNameWithoutExtension(processName);
            foreach (System.Diagnostics.Process process in System.Diagnostics.Process.GetProcessesByName(baseName))
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero)
                    {
                        return process.MainModule.FileName;
                    }
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return null;
        }
        private void ToggleTokeEffect()
        {
            bool effectActive = _manualLocked ||
                (_settings.AutoLockEnabled && !string.IsNullOrEmpty(_matchedProcess));
            bool enableEffect = !effectActive;

            bool previousAutoLock = _settings.AutoLockEnabled;
            bool previousManualLock = _manualLocked;
            string previousMatchedProcess = _matchedProcess;

            _settings.AutoLockEnabled = enableEffect;
            _manualLocked = enableEffect;
            if (!enableEffect)
            {
                _matchedProcess = null;
            }

            try
            {
                _settings.Save();
            }
            catch (Exception exception)
            {
                _settings.AutoLockEnabled = previousAutoLock;
                _manualLocked = previousManualLock;
                _matchedProcess = previousMatchedProcess;
                MessageBox.Show(
                    "Failed to save TokeIME effect state: \r\n" + exception.Message,
                    "TokeIME",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.SetAutoLockEnabled(enableEffect);
            }

            RefreshLockState();
            if (enableEffect)
            {
                ToastForm.ShowFor(_settingsForm, "Effect Enabled", Color.FromArgb(22, 163, 74));
            }
            else
            {
                ToastForm.ShowFor(_settingsForm, "Effect Disabled", Color.FromArgb(71, 85, 105));
            }
        }

        private void AutoLockItemClick(object sender, EventArgs e)
        {
            bool previousValue = _settings.AutoLockEnabled;
            _effectEnabledItem.Checked = !_effectEnabledItem.Checked;
            _settings.AutoLockEnabled = _effectEnabledItem.Checked;

            try
            {
                _settings.Save();
            }
            catch (Exception exception)
            {
                _settings.AutoLockEnabled = previousValue;
                _effectEnabledItem.Checked = previousValue;
                MessageBox.Show(
                    "Failed to save settings: \r\n" + exception.Message,
                    "TokeIME",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }

            if (!_settings.AutoLockEnabled)
            {
                _matchedProcess = null;
            }
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.SetAutoLockEnabled(_settings.AutoLockEnabled);
            }
            RefreshLockState();
        }

        private void RefreshLockState()
        {
            bool shouldLock = _manualLocked || !string.IsNullOrEmpty(_matchedProcess);
            _keyboardLock.IsLocked = shouldLock;

            _notifyIcon.Icon = shouldLock ? _activeIcon : _inactiveIcon;

            _effectEnabledItem.Checked = _settings.AutoLockEnabled;
            UpdateMenuState();

            string reason = _manualLocked
                ? "Manual lock"
                : (!string.IsNullOrEmpty(_matchedProcess) ? _matchedProcess : string.Empty);
            string tooltip = TruncateTooltip(
                shouldLock
                    ? "TokeIME - Locked" + (string.IsNullOrEmpty(reason) ? string.Empty : "（" + reason + "）")
                    : "TokeIME - Unlocked");
            if (_notifyIcon.Text != tooltip)
            {
                _notifyIcon.Text = tooltip;
            }
        }

        private void UpdateMenuState()
        {
            bool locked = _manualLocked || !string.IsNullOrEmpty(_matchedProcess);
            if (locked)
            {
                string detail = _manualLocked ? "Manual lock" : _matchedProcess;
                _statusItem.Text = "Status: Locked (" + detail + "）";
            }
            else
            {
                _statusItem.Text = "Status: Unlocked";
            }


            _effectEnabledItem.Checked = _settings.AutoLockEnabled;
        }

        private void ShowSettings()
        {
            if (_settingsOpen)
            {
                if (_settingsForm != null && !_settingsForm.IsDisposed)
                {
                    _settingsForm.Activate();
                    _settingsForm.BringToFront();
                }
                return;
            }

            _settingsOpen = true;
            if (_autoAddPrompt != null && !_autoAddPrompt.IsDisposed)
            {
                _autoAddPrompt.Close();
            }

            try
            {
                ShowSettingsCore();
            }
            finally
            {
                _settingsOpen = false;
            }
        }

        private void ShowSettingsCore()
        {
            using (SettingsForm form = new SettingsForm(_settings, SaveSettingsFromForm))
            {
                _settingsForm = form;
                try
                {
                    form.ShowDialog();
                }
                finally
                {
                    _settingsForm = null;
                }
            }
        }

        private bool SaveSettingsFromForm(AppSettings updated)
        {
            AppSettings previous = _settings;
            try
            {
                if (updated.StartWithWindows != previous.StartWithWindows)
                {
                    StartupManager.SetEnabled(updated.StartWithWindows);
                }
                updated.Save();
            }
            catch (Exception exception)
            {
                try
                {
                    StartupManager.SetEnabled(previous.StartWithWindows);
                }
                catch
                {
                    // Best-effort rollback only.
                }

                MessageBox.Show(
                    "Failed to save settings: \r\n" + exception.Message,
                    "TokeIME",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return false;
            }

            _settings = updated;
            _keyboardLock.UpdateOptions(_settings);
            _matchedProcess = null;
            RefreshLockState();
            return true;
        }

        private void OpenConfigFile()
        {
            try
            {
                _settings.Save();
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = AppSettings.ConfigPath,
                    UseShellExecute = true
                });
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "Unable to open the config file: \r\n" + exception.Message + "\r\n\r\nPath: \r\n" + AppSettings.ConfigPath,
                    "TokeIME",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }


        private void ExitApplication()
        {
            Dispose();
            ExitThread();
        }

        private static string TruncateTooltip(string value)
        {
            if (value != null && value.Length > 63)
            {
                return value.Substring(0, 60) + "...";
            }
            return value;
        }

        private static Icon LoadIconResource(string resourceName, out bool ownsIcon)
        {
            ownsIcon = false;
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
                {
                    if (stream != null)
                    {
                        using (Icon sourceIcon = new Icon(stream))
                        {
                            Icon clone = (Icon)sourceIcon.Clone();
                            ownsIcon = true;
                            return clone;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to a standard icon.
            }

            return SystemIcons.Application;
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                _disposed = true;

                if (_autoAddPrompt != null && !_autoAddPrompt.IsDisposed)
                {
                    _autoAddPrompt.Close();
                }
                _foregroundTimer.Stop();
                _foregroundTimer.Dispose();
                if (_showSettingsEvent != null)
                {
                    _showSettingsEvent.Close();
                    _showSettingsEvent.Dispose();
                }
                _keyboardLock.Dispose();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _menu.Dispose();

                if (_ownsInactiveIcon)
                {
                    _inactiveIcon.Dispose();
                }
                if (_ownsActiveIcon)
                {
                    _activeIcon.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
