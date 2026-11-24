using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TokeIME
{
    internal static class NativeMethods
    {
        internal const int WH_KEYBOARD_LL = 13;
        internal const int WM_KEYDOWN = 0x0100;
        internal const int WM_KEYUP = 0x0101;
        internal const int WM_SYSKEYDOWN = 0x0104;
        internal const int WM_SYSKEYUP = 0x0105;

        internal const int VK_SHIFT = 0x10;
        internal const int VK_CONTROL = 0x11;
        internal const int VK_MENU = 0x12;
        internal const int VK_CAPITAL = 0x14;
        internal const int VK_SPACE = 0x20;
        internal const int VK_LWIN = 0x5B;
        internal const int VK_RWIN = 0x5C;
        internal const int VK_LSHIFT = 0xA0;
        internal const int VK_LCONTROL = 0xA2;
        internal const int VK_RCONTROL = 0xA3;
        internal const int VK_LMENU = 0xA4;
        internal const int VK_RMENU = 0xA5;
        internal const int VK_RSHIFT = 0xA1;

        internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        internal struct KbdLlHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr SetWindowsHookEx(
            int idHook,
            LowLevelKeyboardProc lpfn,
            IntPtr hMod,
            uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr CallNextHookEx(
            IntPtr hhk,
            int nCode,
            IntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        internal static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        internal static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool IsIconic(IntPtr hWnd);

        [DllImport("dwmapi.dll")]
        internal static extern int DwmGetWindowAttribute(
            IntPtr hWnd,
            int attribute,
            out int value,
            int valueSize);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern IntPtr GetModuleHandle(string moduleName);

        internal static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        internal static bool IsMainWindowVisible(IntPtr windowHandle)
        {
            if (windowHandle == IntPtr.Zero ||
                !IsWindowVisible(windowHandle) ||
                IsIconic(windowHandle))
            {
                return false;
            }

            try
            {
                int cloaked;
                if (DwmGetWindowAttribute(windowHandle, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0)
                {
                    return false;
                }
            }
            catch
            {
            }
            return true;
        }

        internal static string GetForegroundProcessName()
        {
            IntPtr foregroundWindow = GetForegroundWindow();
            if (!IsMainWindowVisible(foregroundWindow))
            {
                return string.Empty;
            }

            uint processId;
            GetWindowThreadProcessId(foregroundWindow, out processId);
            if (processId == 0)
            {
                return string.Empty;
            }

            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    return process.ProcessName + ".exe";
                }
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (InvalidOperationException)
            {
                return string.Empty;
            }
            catch (Win32Exception)
            {
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
