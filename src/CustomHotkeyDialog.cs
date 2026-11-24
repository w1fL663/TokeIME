using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TokeIME
{
    internal sealed class CustomHotkeyDialog : Form
    {
        private readonly List<HotkeyDefinition> _existingHotkeys;
        private readonly CheckBox _ctrlCheckBox;
        private readonly CheckBox _altCheckBox;
        private readonly CheckBox _shiftCheckBox;
        private readonly CheckBox _winCheckBox;
        private readonly Button _keyCaptureButton;
        private readonly Label _previewLabel;
        private int _capturedVirtualKey;

        public HotkeyDefinition Result { get; private set; }

        public CustomHotkeyDialog(IEnumerable<HotkeyDefinition> existingHotkeys)
        {
            _existingHotkeys = new List<HotkeyDefinition>();
            if (existingHotkeys != null)
            {
                foreach (HotkeyDefinition hotkey in existingHotkeys)
                {
                    if (hotkey != null)
                    {
                        _existingHotkeys.Add(hotkey.Clone());
                    }
                }
            }

            Text = "Add Custom Shortcut";
            Font = SystemFonts.MessageBoxFont;
            UiAutoScale.Prepare(this);
            ClientSize = new Size(420, 300);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;

            Label titleLabel = new Label();
            titleLabel.Text = "Custom Blocked Shortcut";
            titleLabel.Font = new Font(Font.FontFamily, Math.Min(Font.Size + 6F, 23F), FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.Location = new Point(18, 16);
            titleLabel.Size = new Size(370, 34);
            Controls.Add(titleLabel);

            Label modifierLabel = new Label();
            modifierLabel.Text = "Modifiers";
            modifierLabel.Location = new Point(20, 63);
            modifierLabel.Size = new Size(100, 22);
            Controls.Add(modifierLabel);

            _ctrlCheckBox = CreateModifierCheckBox("Ctrl", 20);
            _altCheckBox = CreateModifierCheckBox("Alt", 110);
            _shiftCheckBox = CreateModifierCheckBox("Shift", 200);
            _winCheckBox = CreateModifierCheckBox("Win", 290);

            Label keyLabel = new Label();
            keyLabel.Text = "Main Key";
            keyLabel.Location = new Point(20, 126);
            keyLabel.Size = new Size(100, 22);
            Controls.Add(keyLabel);

            _keyCaptureButton = new Button();
            _keyCaptureButton.Text = "Press a key";
            _keyCaptureButton.Font = new Font(Font.FontFamily, Math.Min(Font.Size + 2F, 16F), FontStyle.Bold, GraphicsUnit.Point);
            _keyCaptureButton.Location = new Point(20, 150);
            _keyCaptureButton.Size = new Size(378, 30);
            _keyCaptureButton.KeyDown += KeyCaptureButtonKeyDown;
            Controls.Add(_keyCaptureButton);

            Label previewTitle = new Label();
            previewTitle.Text = "Preview";
            previewTitle.Location = new Point(20, 194);
            previewTitle.Size = new Size(60, 22);
            Controls.Add(previewTitle);

            _previewLabel = new Label();
            _previewLabel.Text = string.Empty;
            _previewLabel.Font = new Font(Font.FontFamily, Math.Min(Font.Size + 2F, 16F), FontStyle.Bold, GraphicsUnit.Point);
            _previewLabel.ForeColor = ColorTranslator.FromHtml("#0F766E");
            _previewLabel.Location = new Point(78, 189);
            _previewLabel.Size = new Size(320, 30);
            Controls.Add(_previewLabel);

            Button addButton = new Button();
            addButton.Text = "Add";
            addButton.Location = new Point(218, 250);
            addButton.Size = new Size(84, 30);
            addButton.Click += delegate { Confirm(); };
            Controls.Add(addButton);

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Location = new Point(314, 250);
            cancelButton.Size = new Size(84, 30);
            cancelButton.DialogResult = DialogResult.Cancel;
            Controls.Add(cancelButton);

            _ctrlCheckBox.CheckedChanged += delegate { UpdatePreview(); };
            _altCheckBox.CheckedChanged += delegate { UpdatePreview(); };
            _shiftCheckBox.CheckedChanged += delegate { UpdatePreview(); };
            _winCheckBox.CheckedChanged += delegate { UpdatePreview(); };

            AcceptButton = addButton;
            CancelButton = cancelButton;
            Shown += delegate { _keyCaptureButton.Focus(); };
            UiAutoScale.Enable(this);
        }

        private CheckBox CreateModifierCheckBox(string text, int x)
        {
            CheckBox checkBox = new CheckBox();
            checkBox.Text = text;
            checkBox.Location = new Point(x, 88);
            checkBox.Size = new Size(82, 26);
            Controls.Add(checkBox);
            return checkBox;
        }

        private void KeyCaptureButtonKeyDown(object sender, KeyEventArgs eventArgs)
        {
            eventArgs.SuppressKeyPress = true;
            eventArgs.Handled = true;
            CaptureKey(eventArgs.KeyCode);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (_keyCaptureButton.Focused && (keyData & Keys.KeyCode) == Keys.Tab)
            {
                CaptureKey(Keys.Tab);
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        private void CaptureKey(Keys key)
        {
            if (key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ||
                key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey ||
                key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ||
                key == Keys.LWin || key == Keys.RWin || key == Keys.None)
            {
                return;
            }

            _capturedVirtualKey = (int)key;
            _keyCaptureButton.Text = HotkeyFormatter.GetKeyName(_capturedVirtualKey);
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (_capturedVirtualKey == 0)
            {
                _previewLabel.Text = string.Empty;
                return;
            }

            _previewLabel.Text = HotkeyFormatter.Format(
                _ctrlCheckBox.Checked,
                _altCheckBox.Checked,
                _shiftCheckBox.Checked,
                _winCheckBox.Checked,
                _capturedVirtualKey);
        }

        private void Confirm()
        {
            if (!_ctrlCheckBox.Checked && !_altCheckBox.Checked && !_shiftCheckBox.Checked && !_winCheckBox.Checked)
            {
                MessageBox.Show("Select at least one modifier: Ctrl, Alt, Shift, or Win.", "TokeIME", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (_capturedVirtualKey == 0)
            {
                MessageBox.Show("Click “Press a key”, then press a key.", "TokeIME", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            HotkeyDefinition hotkey = new HotkeyDefinition(
                _ctrlCheckBox.Checked,
                _altCheckBox.Checked,
                _shiftCheckBox.Checked,
                _winCheckBox.Checked,
                _capturedVirtualKey);

            foreach (HotkeyDefinition existing in _existingHotkeys)
            {
                if (hotkey.SameAs(existing))
                {
                    MessageBox.Show("This shortcut is already in the list.", "TokeIME", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            Result = hotkey;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
