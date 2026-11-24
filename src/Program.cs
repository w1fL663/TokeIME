using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace TokeIME
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\TokeIME.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    try
                    {
                        using (EventWaitHandle showSettingsEvent = EventWaitHandle.OpenExisting(TrayApplication.ShowSettingsEventName))
                        {
                            showSettingsEvent.Set();
                        }
                    }
                    catch
                    {
                        MessageBox.Show(
                            "TokeIME is already running. Check the system tray.",
                            "TokeIME",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs eventArgs)
                {
                    MessageBox.Show(
                        "TokeIME encountered an error:\r\n" + eventArgs.Exception.Message,
                        "TokeIME",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                };

                TrayApplication trayApplication = null;
                try
                {
                    trayApplication = new TrayApplication(!HasArgument(args, "--startup"));
                    Application.Run(trayApplication);
                }
                catch (Exception exception)
                {
                    MessageBox.Show(
                        "TokeIME failed to start:\r\n" + exception.Message,
                        "TokeIME",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
                finally
                {
                    if (trayApplication != null)
                    {
                        trayApplication.Dispose();
                    }
                }
            }
        }
        private static bool HasArgument(string[] args, string expected)
        {
            if (args == null)
            {
                return false;
            }
            foreach (string argument in args)
            {
                if (string.Equals(argument, expected, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }

    internal static class UiAutoScale
    {
        public static void Prepare(Form form)
        {
            form.AutoScaleDimensions = new SizeF(96F, 96F);
        }

        public static void Enable(Form form)
        {
            form.AutoScaleMode = AutoScaleMode.Dpi;
            form.Shown += delegate { FitToWorkingArea(form); };
        }

        private static void FitToWorkingArea(Form form)
        {
            if (form.IsDisposed)
            {
                return;
            }

            Screen screen = Screen.FromControl(form);
            Rectangle area = screen.WorkingArea;
            int maxWidth = Math.Max(320, area.Width - 24);
            int maxHeight = Math.Max(240, area.Height - 24);
            float widthFactor = maxWidth / (float)form.Width;
            float heightFactor = maxHeight / (float)form.Height;
            float factor = Math.Min(1F, Math.Min(widthFactor, heightFactor));
            if (factor >= 0.995F)
            {
                return;
            }

            ScaleControlTree(form, factor);
            form.Size = new Size(
                Math.Max(1, (int)Math.Round(form.Width * factor)),
                Math.Max(1, (int)Math.Round(form.Height * factor)));
        }

        private static void ScaleControlTree(Control parent, float factor)
        {
            foreach (Control child in parent.Controls)
            {
                child.Bounds = new Rectangle(
                    (int)Math.Round(child.Left * factor),
                    (int)Math.Round(child.Top * factor),
                    Math.Max(1, (int)Math.Round(child.Width * factor)),
                    Math.Max(1, (int)Math.Round(child.Height * factor)));

                try
                {
                    if (child.Font != null)
                    {
                        child.Font = new Font(
                            child.Font.FontFamily,
                            Math.Max(6F, child.Font.Size * factor),
                            child.Font.Style,
                            GraphicsUnit.Point);
                    }
                }
                catch
                {
                }

                ScaleControlTree(child, factor);
            }
        }
    }

}
