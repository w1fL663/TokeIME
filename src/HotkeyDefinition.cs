using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace TokeIME
{
    [Serializable]
    public sealed class HotkeyDefinition
    {
        public bool Ctrl { get; set; }
        public bool Alt { get; set; }
        public bool Shift { get; set; }
        public bool Win { get; set; }
        public int VirtualKey { get; set; }

        public HotkeyDefinition()
        {
        }

        public HotkeyDefinition(bool ctrl, bool alt, bool shift, bool win, int virtualKey)
        {
            Ctrl = ctrl;
            Alt = alt;
            Shift = shift;
            Win = win;
            VirtualKey = virtualKey;
        }

        [ScriptIgnore]
        public bool HasModifier
        {
            get { return Ctrl || Alt || Shift || Win; }
        }

        [ScriptIgnore]
        public bool IsValid
        {
            get
            {
                return VirtualKey > 0 &&
                       HasModifier &&
                       !IsModifierKey(VirtualKey);
            }
        }

        public HotkeyDefinition Clone()
        {
            return new HotkeyDefinition(Ctrl, Alt, Shift, Win, VirtualKey);
        }

        public bool SameAs(HotkeyDefinition other)
        {
            return other != null &&
                   Ctrl == other.Ctrl &&
                   Alt == other.Alt &&
                   Shift == other.Shift &&
                   Win == other.Win &&
                   VirtualKey == other.VirtualKey;
        }

        public static bool IsModifierKey(int virtualKey)
        {
            return virtualKey == (int)Keys.ControlKey ||
                   virtualKey == (int)Keys.LControlKey ||
                   virtualKey == (int)Keys.RControlKey ||
                   virtualKey == (int)Keys.ShiftKey ||
                   virtualKey == (int)Keys.LShiftKey ||
                   virtualKey == (int)Keys.RShiftKey ||
                   virtualKey == (int)Keys.Menu ||
                   virtualKey == (int)Keys.LMenu ||
                   virtualKey == (int)Keys.RMenu ||
                   virtualKey == (int)Keys.LWin ||
                   virtualKey == (int)Keys.RWin;
        }

        public override string ToString()
        {
            return HotkeyFormatter.Format(this);
        }
    }

    public static class HotkeyFormatter
    {
        public static string Format(HotkeyDefinition hotkey)
        {
            if (hotkey == null)
            {
                return string.Empty;
            }

            return Format(hotkey.Ctrl, hotkey.Alt, hotkey.Shift, hotkey.Win, hotkey.VirtualKey);
        }

        public static string Format(bool ctrl, bool alt, bool shift, bool win, int virtualKey)
        {
            List<string> parts = new List<string>();
            if (ctrl) { parts.Add("Ctrl"); }
            if (alt) { parts.Add("Alt"); }
            if (shift) { parts.Add("Shift"); }
            if (win) { parts.Add("Win"); }
            parts.Add(GetKeyName(virtualKey));
            return string.Join(" + ", parts.ToArray());
        }

        public static string GetKeyName(int virtualKey)
        {
            if (virtualKey >= (int)Keys.A && virtualKey <= (int)Keys.Z)
            {
                return ((char)virtualKey).ToString();
            }
            if (virtualKey >= (int)Keys.D0 && virtualKey <= (int)Keys.D9)
            {
                return ((char)virtualKey).ToString();
            }
            if (virtualKey >= (int)Keys.F1 && virtualKey <= (int)Keys.F24)
            {
                return "F" + (virtualKey - (int)Keys.F1 + 1);
            }
            if (virtualKey >= (int)Keys.NumPad0 && virtualKey <= (int)Keys.NumPad9)
            {
                return "Num " + (virtualKey - (int)Keys.NumPad0);
            }

            switch ((Keys)virtualKey)
            {
                case Keys.Space: return "Space";
                case Keys.Enter: return "Enter";
                case Keys.Tab: return "Tab";
                case Keys.Escape: return "Esc";
                case Keys.Back: return "Backspace";
                case Keys.Delete: return "Delete";
                case Keys.Insert: return "Insert";
                case Keys.Home: return "Home";
                case Keys.End: return "End";
                case Keys.PageUp: return "Page Up";
                case Keys.PageDown: return "Page Down";
                case Keys.Left: return "Left";
                case Keys.Right: return "Right";
                case Keys.Up: return "Up";
                case Keys.Down: return "Down";
                case Keys.OemSemicolon: return ";";
                case Keys.Oemplus: return "+";
                case Keys.Oemcomma: return ",";
                case Keys.OemMinus: return "-";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.Oemtilde: return "`";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemPipe: return "\\";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemQuotes: return "'";
                case Keys.Multiply: return "Num *";
                case Keys.Add: return "Num +";
                case Keys.Subtract: return "Num -";
                case Keys.Divide: return "Num /";
                case Keys.Decimal: return "Num .";
                default: return "VK 0x" + virtualKey.ToString("X2");
            }
        }
    }
}
