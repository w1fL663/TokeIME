using System;
using System.Drawing;
using System.Windows.Forms;

namespace TokeIME
{
    internal sealed class ToastForm : Form
    {
        private readonly Timer _fadeTimer;
        private readonly Timer _holdTimer;
        private readonly Region _roundedRegion;
        private static ToastForm _currentToast;
        private bool _fadingOut;

        private ToastForm(string message, Color backgroundColor)
        {
            Text = "TokeIME Notice";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(210, 68);
            _roundedRegion = new Region(CreateRoundedRectangle(new Rectangle(0, 0, ClientSize.Width, ClientSize.Height), 14F));
            Region = _roundedRegion;
            BackColor = backgroundColor;
            Opacity = 0D;
            Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold, GraphicsUnit.Point);

            Label label = new Label();
            label.Text = message;
            label.ForeColor = Color.White;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Dock = DockStyle.Fill;
            Controls.Add(label);

            _fadeTimer = new Timer();
            _fadeTimer.Interval = 20;
            _fadeTimer.Tick += delegate
            {
                if (!_fadingOut)
                {
                    Opacity = Math.Min(1D, Opacity + 0.08D);
                    if (Opacity >= 1D)
                    {
                        _fadeTimer.Stop();
                        _holdTimer.Start();
                    }
                }
                else
                {
                    Opacity = Math.Max(0D, Opacity - 0.08D);
                    if (Opacity <= 0D)
                    {
                        _fadeTimer.Stop();
                        Close();
                    }
                }
            };

            _holdTimer = new Timer();
            _holdTimer.Interval = 1800;
            _holdTimer.Tick += delegate
            {
                _holdTimer.Stop();
                _fadingOut = true;
                _fadeTimer.Start();
            };

            Shown += delegate { _fadeTimer.Start(); };
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedRectangle(Rectangle rectangle, float radius)
        {
            float diameter = radius * 2F;
            System.Drawing.Drawing2D.GraphicsPath path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
            path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        public static void ShowFor(Form owner, string message)
        {
            ShowFor(owner, message, Color.FromArgb(22, 163, 74));
        }

        public static void ShowFor(Form owner, string message, Color backgroundColor)
        {
            if (_currentToast != null && !_currentToast.IsDisposed)
            {
                _currentToast.Close();
            }

            ToastForm toast = new ToastForm(message, backgroundColor);
            _currentToast = toast;
            toast.FormClosed += delegate
            {
                if (_currentToast == toast)
                {
                    _currentToast = null;
                }
            };
            Rectangle area = owner == null ? Screen.PrimaryScreen.WorkingArea : Screen.FromControl(owner).WorkingArea;
            int x = owner == null
                ? area.Right - toast.Width - 24
                : Math.Min(owner.Right - toast.Width - 20, area.Right - toast.Width - 12);
            int y = owner == null
                ? area.Bottom - toast.Height - 24
                : Math.Min(owner.Bottom - toast.Height - 70, area.Bottom - toast.Height - 12);
            toast.Location = new Point(Math.Max(area.Left + 12, x), Math.Max(area.Top + 12, y));
            toast.Show();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_fadeTimer != null)
                {
                    _fadeTimer.Stop();
                    _fadeTimer.Dispose();
                }
                if (_roundedRegion != null)
                {
                    _roundedRegion.Dispose();
                }
                if (_holdTimer != null)
                {
                    _holdTimer.Stop();
                    _holdTimer.Dispose();
                }
            }
            base.Dispose(disposing);
        }
    }
}
