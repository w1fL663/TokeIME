using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace TokeIME
{
    internal sealed class KeyboardLock : IDisposable
    {
        private readonly NativeMethods.LowLevelKeyboardProc _hookCallback;
        private readonly System.Collections.Generic.HashSet<int> _blockedKeys;
        private readonly System.Collections.Generic.List<HotkeyDefinition> _customHotkeys;
        private IntPtr _hookHandle;
        private bool _disposed;
        private bool _isLocked;

        private bool _blockCtrlShift;
        private bool _blockAltShift;
        private bool _blockCtrlSpace;
        private bool _blockWinSpace;
        private bool _blockCapsLock;
        private bool _customHotkeysUseWin;
        private bool _ctrlDown;
        private bool _shiftDown;
        private bool _altDown;
        private bool _winDown;


        public KeyboardLock()
        {
            _hookCallback = HookCallback;
            _blockedKeys = new System.Collections.Generic.HashSet<int>();
            _customHotkeys = new System.Collections.Generic.List<HotkeyDefinition>();
        }

        public bool IsLocked
        {
            get { return _isLocked; }
            set
            {
                if (_isLocked == value)
                {
                    return;
                }

                _isLocked = value;
                if (!_isLocked)
                {
                    _blockedKeys.Clear();
                }
            }
        }

        public void UpdateOptions(AppSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _blockCtrlShift = settings.BlockCtrlShift;
            _blockAltShift = settings.BlockAltShift;
            _blockCtrlSpace = settings.BlockCtrlSpace;
            _blockWinSpace = settings.BlockWinSpace;
            _blockCapsLock = settings.BlockCapsLock;

            _customHotkeys.Clear();
            _customHotkeysUseWin = false;
            if (settings.CustomHotkeys != null)
            {
                for (int index = 0; index < settings.CustomHotkeys.Count; index++)
                {
                    HotkeyDefinition hotkey = settings.CustomHotkeys[index];
                    if (hotkey == null || !hotkey.IsValid)
                    {
                        continue;
                    }

                    _customHotkeys.Add(hotkey.Clone());
                    if (hotkey.Win)
                    {
                        _customHotkeysUseWin = true;
                    }
                }
            }
        }

        public void Install()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                return;
            }

            IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);
            _hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _hookCallback,
                moduleHandle,
                0);

            if (_hookHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Unable to install the global keyboard hook.");
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                try
                {
                    NativeMethods.KbdLlHookStruct data =
                        (NativeMethods.KbdLlHookStruct)Marshal.PtrToStructure(
                            lParam,
                            typeof(NativeMethods.KbdLlHookStruct));

                    int virtualKey = (int)data.vkCode;
                    bool keyDown = wParam == (IntPtr)NativeMethods.WM_KEYDOWN ||
                                   wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN;
                    bool keyUp = wParam == (IntPtr)NativeMethods.WM_KEYUP ||
                                 wParam == (IntPtr)NativeMethods.WM_SYSKEYUP;

                    UpdateModifierState(virtualKey, keyDown, keyUp);

                    if (_isLocked && (keyDown || keyUp) && ShouldBlock(virtualKey, keyDown))
                    {
                        return (IntPtr)1;
                    }
                }
                catch
                {
                    // Never let a managed exception break the global input chain.
                }
            }

            return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private bool ShouldBlock(int virtualKey, bool keyDown)
        {
            if (_blockCapsLock && virtualKey == NativeMethods.VK_CAPITAL)
            {
                if (keyDown)
                {
                    _blockedKeys.Add(virtualKey);
                }
                else
                {
                    _blockedKeys.Remove(virtualKey);
                }
                return true;
            }

            if (_blockedKeys.Contains(virtualKey))
            {
                if (!keyDown)
                {
                    _blockedKeys.Remove(virtualKey);
                }
                return true;
            }

            if (_customHotkeysUseWin && (virtualKey == NativeMethods.VK_LWIN || virtualKey == NativeMethods.VK_RWIN))
            {
                if (keyDown)
                {
                    _blockedKeys.Add(virtualKey);
                }
                return true;
            }

            if (MatchesCustomHotkey(virtualKey))
            {
                if (keyDown)
                {
                    _blockedKeys.Add(virtualKey);
                }
                return true;
            }

            bool ctrlDown = NativeMethods.IsKeyDown(NativeMethods.VK_CONTROL);
            bool altDown = NativeMethods.IsKeyDown(NativeMethods.VK_MENU);
            bool winDown = NativeMethods.IsKeyDown(NativeMethods.VK_LWIN) ||
                           NativeMethods.IsKeyDown(NativeMethods.VK_RWIN);

            bool block = false;
            if (_blockCtrlShift && ctrlDown && IsShiftKey(virtualKey))
            {
                block = true;
            }
            else if (_blockAltShift && altDown && IsShiftKey(virtualKey))
            {
                block = true;
            }
            else if (_blockCtrlSpace && ctrlDown && virtualKey == NativeMethods.VK_SPACE)
            {
                block = true;
            }
            else if (_blockWinSpace && winDown && virtualKey == NativeMethods.VK_SPACE)
            {
                block = true;
            }

            if (block && keyDown)
            {
                _blockedKeys.Add(virtualKey);
            }

            return block;
        }

        private void UpdateModifierState(int virtualKey, bool keyDown, bool keyUp)
        {
            if (!keyDown && !keyUp)
            {
                return;
            }

            bool isDown = keyDown;
            if (virtualKey == NativeMethods.VK_CONTROL ||
                virtualKey == NativeMethods.VK_LCONTROL ||
                virtualKey == NativeMethods.VK_RCONTROL)
            {
                _ctrlDown = isDown;
            }
            else if (virtualKey == NativeMethods.VK_SHIFT ||
                     virtualKey == NativeMethods.VK_LSHIFT ||
                     virtualKey == NativeMethods.VK_RSHIFT)
            {
                _shiftDown = isDown;
            }
            else if (virtualKey == NativeMethods.VK_MENU ||
                     virtualKey == NativeMethods.VK_LMENU ||
                     virtualKey == NativeMethods.VK_RMENU)
            {
                _altDown = isDown;
            }
            else if (virtualKey == NativeMethods.VK_LWIN || virtualKey == NativeMethods.VK_RWIN)
            {
                _winDown = isDown;
            }
        }
        private bool MatchesCustomHotkey(int virtualKey)
        {
            if (_customHotkeys.Count == 0)
            {
                return false;
            }

            bool ctrlDown = _ctrlDown;
            bool altDown = _altDown;
            bool shiftDown = _shiftDown;
            bool winDown = _winDown;

            for (int index = 0; index < _customHotkeys.Count; index++)
            {
                HotkeyDefinition hotkey = _customHotkeys[index];
                if (hotkey.VirtualKey == virtualKey &&
                    hotkey.Ctrl == ctrlDown &&
                    hotkey.Alt == altDown &&
                    hotkey.Shift == shiftDown &&
                    hotkey.Win == winDown)
                {
                    return true;
                }
            }

            return false;
        }
        private static bool IsShiftKey(int virtualKey)
        {
            return virtualKey == NativeMethods.VK_SHIFT ||
                   virtualKey == NativeMethods.VK_LSHIFT ||
                   virtualKey == NativeMethods.VK_RSHIFT;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _isLocked = false;
            _blockedKeys.Clear();

            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }
}
