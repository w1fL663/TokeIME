using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace TokeIME
{
    internal sealed class AutoAddPromptForm : Form
    {
        private readonly Timer _closeTimer;
        private readonly Icon _icon;
        private readonly Bitmap _iconBitmap;
        private readonly bool _ownsIcon;

        public event Action Accepted;
        public event Action Rejected;

        public string ProgramName { get; private set; }
        public string ExecutablePath { get; private set; }

        public AutoAddPromptForm(string programName, string executablePath)
        {
            ProgramName = programName;
            ExecutablePath = executablePath;

            Text = "TokeIME Auto Add";
            UiAutoScale.Prepare(this);
            ClientSize = new Size(440, 154);
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Font = SystemFonts.MessageBoxFont;
            BackColor = Color.White;

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Location = new Point(area.Right - Width - 18, area.Bottom - Height - 18);

            PictureBox iconBox = new PictureBox();
            iconBox.Location = new Point(16, 18);
            iconBox.Size = new Size(48, 48);
            iconBox.SizeMode = PictureBoxSizeMode.Zoom;
            Controls.Add(iconBox);

            _icon = LoadProgramIcon(executablePath, out _ownsIcon);
            _iconBitmap = _icon.ToBitmap();
            iconBox.Image = _iconBitmap;

            Label titleLabel = new Label();
            titleLabel.Text = "New program detected";
            titleLabel.Font = new Font(Font.FontFamily, Math.Min(Font.Size + 1F, 16F), FontStyle.Bold, GraphicsUnit.Point);
            titleLabel.Location = new Point(76, 16);
            titleLabel.Size = new Size(340, 24);
            Controls.Add(titleLabel);

            Label nameLabel = new Label();
            nameLabel.Text = programName;
            nameLabel.Font = new Font(Font.FontFamily, Math.Min(Font.Size + 2F, 16F), FontStyle.Bold, GraphicsUnit.Point);
            nameLabel.Location = new Point(76, 42);
            nameLabel.Size = new Size(340, 24);
            Controls.Add(nameLabel);


            Button addButton = new Button();
            addButton.Text = "Add to list";
            addButton.Location = new Point(216, 104);
            addButton.Size = new Size(96, 30);
            addButton.Click += delegate
            {
                if (Accepted != null)
                {
                    Accepted();
                }
                Close();
            };
            Controls.Add(addButton);

            Button skipButton = new Button();
            skipButton.Text = "Not now";
            skipButton.Location = new Point(320, 104);
            skipButton.Size = new Size(96, 30);
            skipButton.Click += delegate
            {
                if (Rejected != null)
                {
                    Rejected();
                }
                Close();
            };
            Controls.Add(skipButton);

            _closeTimer = new Timer();
            _closeTimer.Interval = 15000;
            _closeTimer.Tick += delegate
            {
                _closeTimer.Stop();
                Close();
            };
            _closeTimer.Start();
            UiAutoScale.Enable(this);
            Shown += delegate
            {
                Rectangle workArea = Screen.FromControl(this).WorkingArea;
                Location = new Point(workArea.Right - Width - 18, workArea.Bottom - Height - 18);
            };
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_closeTimer != null)
                {
                    _closeTimer.Stop();
                    _closeTimer.Dispose();
                }
                if (_iconBitmap != null)
                {
                    _iconBitmap.Dispose();
                }
                if (_ownsIcon && _icon != null)
                {
                    _icon.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private static Icon LoadProgramIcon(string executablePath, out bool ownsIcon)
        {
            ownsIcon = false;
            if (!string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath))
            {
                try
                {
                    Icon icon = Icon.ExtractAssociatedIcon(executablePath);
                    if (icon != null)
                    {
                        ownsIcon = true;
                        return icon;
                    }
                }
                catch
                {
                }
            }
            return SystemIcons.Application;
        }
    }
}
