using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TokeIME
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _draft;
        private readonly Func<AppSettings, bool> _saveHandler;
        private readonly ListBox _processList;
        private readonly ContextMenuStrip _processMenu;
        private readonly ContextMenuStrip _customHotkeyMenu;
        private readonly CheckBox _autoAddPromptCheckBox;
        private readonly CheckBox _autoLockCheckBox;
        private readonly CheckBox _ctrlShiftCheckBox;
        private readonly CheckBox _altShiftCheckBox;
        private readonly CheckBox _ctrlSpaceCheckBox;
        private readonly CheckBox _winSpaceCheckBox;
        private readonly CheckBox _capsLockCheckBox;
        private readonly CheckBox _startupCheckBox;
        private readonly ListBox _customHotkeyList;

        public AppSettings Result { get; private set; }

        internal void SetAutoLockEnabled(bool enabled)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<bool>(SetAutoLockEnabled), enabled);
                return;
            }

            _draft.AutoLockEnabled = enabled;
            _autoLockCheckBox.Checked = enabled;
        }

        internal void AddDetectedProgram(string processName)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AddDetectedProgram), processName);
                return;
            }

            string normalized = AppSettings.NormalizeProcessName(processName);
            if (normalized == null)
            {
                return;
            }

            AddProcess(normalized);
            if (!_draft.Processes.Exists(delegate(string existing) { return string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase); }))
            {
                _draft.Processes.Add(normalized);
            }
        }

        public SettingsForm(AppSettings settings)
            : this(settings, null)
        {
        }

        public SettingsForm(AppSettings settings, Func<AppSettings, bool> saveHandler)
        {
            _draft = settings.Clone();
            _saveHandler = saveHandler;

            Text = "TokeIME Settings";
            Icon = LoadApplicationIcon();
            ShowIcon = true;
            Font = SystemFonts.MessageBoxFont;
            UiAutoScale.Prepare(this);
            ClientSize = new Size(624, 732);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;

            GroupBox gamesGroup = new GroupBox();
            gamesGroup.Text = "Program Auto-Lock";
            gamesGroup.Location = new Point(12, 12);
            gamesGroup.Size = new Size(600, 274);
            Controls.Add(gamesGroup);

            _autoLockCheckBox = new CheckBox();
            _autoLockCheckBox.Text = "Enable TokeIME";
            _autoLockCheckBox.Location = new Point(18, 25);
            _autoLockCheckBox.Size = new Size(330, 24);
            _autoLockCheckBox.Checked = _draft.AutoLockEnabled;
            gamesGroup.Controls.Add(_autoLockCheckBox);

            Label processLabel = new Label();
            processLabel.Text = "Added Programs (right-click to remove)";
            processLabel.Location = new Point(18, 58);
            processLabel.Size = new Size(450, 22);
            gamesGroup.Controls.Add(processLabel);

            _processList = new ListBox();
            _processList.Location = new Point(18, 84);
            _processList.Size = new Size(562, 112);
            _processList.IntegralHeight = false;
            foreach (string processName in _draft.Processes)
            {
                _processList.Items.Add(processName);
            }
            gamesGroup.Controls.Add(_processList);

            _processMenu = new ContextMenuStrip();
            ToolStripMenuItem removeItem = new ToolStripMenuItem("Remove");
            removeItem.Click += delegate { RemoveSelectedProcess(); };
            _processMenu.Items.Add(removeItem);
            _processMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs eventArgs)
            {
                Point point = _processList.PointToClient(Cursor.Position);
                if (_processList.IndexFromPoint(point) < 0)
                {
                    eventArgs.Cancel = true;
                }
            };
            _processList.ContextMenuStrip = _processMenu;
            _processList.MouseDown += delegate(object sender, MouseEventArgs eventArgs)
            {
                if (eventArgs.Button == MouseButtons.Right)
                {
                    int index = _processList.IndexFromPoint(eventArgs.Location);
                    if (index >= 0)
                    {
                        _processList.SelectedIndex = index;
                    }
                }
            };

            Button autoAddButton = new Button();
            autoAddButton.Text = "Add Running";
            autoAddButton.Location = new Point(18, 207);
            autoAddButton.Size = new Size(130, 28);
            autoAddButton.Click += delegate { SearchForPrograms(SoftwareSearchScope.Running); };
            gamesGroup.Controls.Add(autoAddButton);

            Button globalSearchButton = new Button();
            globalSearchButton.Text = "Global Search";
            globalSearchButton.Location = new Point(160, 207);
            globalSearchButton.Size = new Size(210, 28);
            globalSearchButton.Click += delegate { SearchForPrograms(SoftwareSearchScope.Global); };
            gamesGroup.Controls.Add(globalSearchButton);

            Button desktopSearchButton = new Button();
            desktopSearchButton.Text = "Desktop Search";
            desktopSearchButton.Location = new Point(382, 207);
            desktopSearchButton.Size = new Size(198, 28);
            desktopSearchButton.Click += delegate { SearchForPrograms(SoftwareSearchScope.Desktop); };
            gamesGroup.Controls.Add(desktopSearchButton);

            GroupBox shortcutsGroup = new GroupBox();
            shortcutsGroup.Text = "Shortcut Blocking";
            shortcutsGroup.Location = new Point(12, 296);
            shortcutsGroup.Size = new Size(600, 270);
            Controls.Add(shortcutsGroup);

            _ctrlShiftCheckBox = CreateShortcutCheckBox("Ctrl + Shift", 18, 28, shortcutsGroup);
            _ctrlShiftCheckBox.Checked = _draft.BlockCtrlShift;
            _altShiftCheckBox = CreateShortcutCheckBox("Alt + Shift", 308, 28, shortcutsGroup);
            _altShiftCheckBox.Checked = _draft.BlockAltShift;
            _ctrlSpaceCheckBox = CreateShortcutCheckBox("Ctrl + Space", 18, 66, shortcutsGroup);
            _ctrlSpaceCheckBox.Checked = _draft.BlockCtrlSpace;
            _winSpaceCheckBox = CreateShortcutCheckBox("Win + Space", 308, 66, shortcutsGroup);
            _winSpaceCheckBox.Checked = _draft.BlockWinSpace;
            _capsLockCheckBox = CreateShortcutCheckBox("Caps Lock", 18, 104, shortcutsGroup);
            _capsLockCheckBox.Checked = _draft.BlockCapsLock;

            Label customLabel = new Label();
            customLabel.Text = "Custom Shortcuts";
            customLabel.Location = new Point(18, 143);
            customLabel.Size = new Size(500, 22);
            shortcutsGroup.Controls.Add(customLabel);

            _customHotkeyList = new ListBox();
            _customHotkeyList.Location = new Point(18, 166);
            _customHotkeyList.Size = new Size(396, 44);
            _customHotkeyList.IntegralHeight = false;
            _customHotkeyList.HorizontalScrollbar = true;
            foreach (HotkeyDefinition hotkey in _draft.CustomHotkeys)
            {
                _customHotkeyList.Items.Add(hotkey.Clone());
            }
            shortcutsGroup.Controls.Add(_customHotkeyList);

            _customHotkeyMenu = new ContextMenuStrip();
            ToolStripMenuItem removeHotkeyItem = new ToolStripMenuItem("Remove");
            removeHotkeyItem.Click += delegate { RemoveSelectedCustomHotkey(); };
            _customHotkeyMenu.Items.Add(removeHotkeyItem);
            _customHotkeyMenu.Opening += delegate(object sender, System.ComponentModel.CancelEventArgs eventArgs)
            {
                Point point = _customHotkeyList.PointToClient(Cursor.Position);
                if (_customHotkeyList.IndexFromPoint(point) < 0)
                {
                    eventArgs.Cancel = true;
                }
            };
            _customHotkeyList.ContextMenuStrip = _customHotkeyMenu;
            _customHotkeyList.MouseDown += delegate(object sender, MouseEventArgs eventArgs)
            {
                if (eventArgs.Button == MouseButtons.Right)
                {
                    int index = _customHotkeyList.IndexFromPoint(eventArgs.Location);
                    if (index >= 0)
                    {
                        _customHotkeyList.SelectedIndex = index;
                    }
                }
            };

            Button addCustomButton = new Button();
            addCustomButton.Text = "Add Custom...";
            addCustomButton.Location = new Point(430, 164);
            addCustomButton.Size = new Size(152, 27);
            addCustomButton.Click += delegate { AddCustomHotkey(); };
            shortcutsGroup.Controls.Add(addCustomButton);

            GroupBox otherGroup = new GroupBox();
            otherGroup.Text = "Startup & Alerts";
            otherGroup.Location = new Point(12, 578);
            otherGroup.Size = new Size(600, 104);
            Controls.Add(otherGroup);

            _startupCheckBox = new CheckBox();
            _startupCheckBox.Text = "Run in the tray after Windows sign-in";
            _startupCheckBox.Location = new Point(18, 24);
            _startupCheckBox.Size = new Size(330, 24);
            _startupCheckBox.Checked = _draft.StartWithWindows;
            otherGroup.Controls.Add(_startupCheckBox);

            _autoAddPromptCheckBox = new CheckBox();
            _autoAddPromptCheckBox.Text = "Prompt to add newly opened programs";
            _autoAddPromptCheckBox.Location = new Point(18, 50);
            _autoAddPromptCheckBox.Size = new Size(330, 24);
            _autoAddPromptCheckBox.Checked = _draft.AutoAddPromptEnabled;
            otherGroup.Controls.Add(_autoAddPromptCheckBox);


            Button saveButton = new Button();
            saveButton.Text = "Save";
            saveButton.Location = new Point(426, 694);
            saveButton.Size = new Size(88, 30);
            saveButton.Click += delegate { SaveSettings(); };
            Controls.Add(saveButton);

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Location = new Point(520, 694);
            cancelButton.Size = new Size(92, 30);
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);

            AcceptButton = saveButton;
            CancelButton = cancelButton;
            FormClosed += delegate { _processMenu.Dispose(); _customHotkeyMenu.Dispose(); };
            UiAutoScale.Enable(this);
        }

        private static Icon LoadApplicationIcon()
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    if (icon != null)
                    {
                        return (Icon)icon.Clone();
                    }
                }
            }
            catch
            {
            }
            return SystemIcons.Application;
        }
        private static CheckBox CreateShortcutCheckBox(string text, int x, int y, Control parent)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Text = text;
            checkBox.Location = new Point(x, y);
            checkBox.Size = new Size(230, 24);
            parent.Controls.Add(checkBox);
            return checkBox;
        }


        private bool AddProcess(string value)
        {
            string processName = AppSettings.NormalizeProcessName(value);
            if (processName == null)
            {
                MessageBox.Show("Enter a valid program name, for example game.exe.", "TokeIME", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            for (int index = 0; index < _processList.Items.Count; index++)
            {
                if (string.Equals(
                    _processList.Items[index].ToString(),
                    processName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    _processList.SelectedIndex = index;
                    return false;
                }
            }

            int newIndex = _processList.Items.Add(processName);
            _processList.SelectedIndex = newIndex;
            return true;
        }

        private void RemoveSelectedProcess()
        {
            int selectedIndex = _processList.SelectedIndex;
            if (selectedIndex < 0)
            {
                return;
            }

            _processList.Items.RemoveAt(selectedIndex);
            if (_processList.Items.Count > 0)
            {
                _processList.SelectedIndex = Math.Min(selectedIndex, _processList.Items.Count - 1);
            }
        }


        private void SearchForPrograms(SoftwareSearchScope scope)
        {
            using (SoftwareSearchForm searchForm = new SoftwareSearchForm(string.Empty, scope))
            {
                if (searchForm.ShowDialog(this) == DialogResult.OK && searchForm.SelectedProcessName != null)
                {
                    AddProcess(searchForm.SelectedProcessName);

                }
            }
        }

        private void AddCustomHotkey()
        {
            List<HotkeyDefinition> existing = GetCustomHotkeysFromList();
            using (CustomHotkeyDialog dialog = new CustomHotkeyDialog(existing))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Result != null)
                {
                    int newIndex = _customHotkeyList.Items.Add(dialog.Result.Clone());
                    _customHotkeyList.SelectedIndex = newIndex;
                }
            }
        }

        private void RemoveSelectedCustomHotkey()
        {
            int selectedIndex = _customHotkeyList.SelectedIndex;
            if (selectedIndex < 0)
            {
                return;
            }

            _customHotkeyList.Items.RemoveAt(selectedIndex);
            if (_customHotkeyList.Items.Count > 0)
            {
                _customHotkeyList.SelectedIndex = Math.Min(selectedIndex, _customHotkeyList.Items.Count - 1);
            }
        }

        private List<HotkeyDefinition> GetCustomHotkeysFromList()
        {
            List<HotkeyDefinition> hotkeys = new List<HotkeyDefinition>();
            for (int index = 0; index < _customHotkeyList.Items.Count; index++)
            {
                HotkeyDefinition hotkey = _customHotkeyList.Items[index] as HotkeyDefinition;
                if (hotkey != null && hotkey.IsValid)
                {
                    hotkeys.Add(hotkey.Clone());
                }
            }
            return hotkeys;
        }

        private void SaveSettings()
        {
            _draft.AutoLockEnabled = _autoLockCheckBox.Checked;
            _draft.AutoAddPromptEnabled = _autoAddPromptCheckBox.Checked;
            _draft.BlockCtrlShift = _ctrlShiftCheckBox.Checked;
            _draft.BlockAltShift = _altShiftCheckBox.Checked;
            _draft.BlockCtrlSpace = _ctrlSpaceCheckBox.Checked;
            _draft.BlockWinSpace = _winSpaceCheckBox.Checked;
            _draft.BlockCapsLock = _capsLockCheckBox.Checked;
            _draft.StartWithWindows = _startupCheckBox.Checked;

            List<string> processes = new List<string>();
            for (int index = 0; index < _processList.Items.Count; index++)
            {
                string processName = AppSettings.NormalizeProcessName(_processList.Items[index].ToString());
                if (processName != null)
                {
                    processes.Add(processName);
                }
            }
            _draft.Processes = processes;
            _draft.CustomHotkeys = GetCustomHotkeysFromList();

            Result = _draft;
            if (_saveHandler != null && !_saveHandler(_draft.Clone()))
            {
                return;
            }

            ToastForm.ShowFor(this, "Success");
        }
    }
}
