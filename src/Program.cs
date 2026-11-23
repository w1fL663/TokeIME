using System;
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
}
