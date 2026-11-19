using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using InspectionWidget.Native;

namespace InspectionWidget.Credentials
{
    /// <summary>
    /// 凭据全局快捷键解析、规范化与校验。
    /// 支持 Ctrl / Alt / Shift / Win 组合，或单独 F1-F12；留空表示未绑定。
    /// </summary>
    public static class HotkeySpec
    {
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            uint modifiers;
            uint vk;
            string canonical;
            string error;
            if (TryParse(text, out modifiers, out vk, out canonical, out error))
                return canonical;
            return text.Trim();
        }

        public static string Validate(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;
            uint modifiers;
            uint vk;
            string canonical;
            string error;
            TryParse(text, out modifiers, out vk, out canonical, out error);
            return error;
        }

        public static bool TryParse(
            string text,
            out uint modifiers,
            out uint virtualKey,
            out string canonical,
            out string error)
        {
            modifiers = 0;
            virtualKey = 0;
            canonical = null;
            error = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                error = "快捷键不能为空。";
                return false;
            }

            string[] rawParts = text.Split('+');
            bool hasCtrl = false;
            bool hasAlt = false;
            bool hasShift = false;
            bool hasWin = false;
            string keyToken = null;

            for (int i = 0; i < rawParts.Length; i++)
            {
                string token = rawParts[i].Trim();
                if (token.Length == 0)
                {
                    error = "快捷键格式无效：" + text.Trim();
                    return false;
                }
                string upper = token.ToUpperInvariant();
                if (upper == "CTRL" || upper == "CONTROL")
                {
                    hasCtrl = true;
                }
                else if (upper == "ALT" || upper == "MENU")
                {
                    hasAlt = true;
                }
                else if (upper == "SHIFT")
                {
                    hasShift = true;
                }
                else if (upper == "WIN" || upper == "LWIN" || upper == "RWIN")
                {
                    hasWin = true;
                }
                else
                {
                    if (keyToken != null)
                    {
                        error = "快捷键只能包含一个主键：" + text.Trim();
                        return false;
                    }
                    keyToken = token;
                }
            }

            if (keyToken == null)
            {
                error = "快捷键缺少主键（例如 Ctrl+Alt+1）。";
                return false;
            }

            uint vk;
            string keyDisplay;
            bool isFunctionKey;
            if (!TryMapKeyToken(keyToken, out vk, out keyDisplay, out isFunctionKey, out error))
                return false;

            if (!hasCtrl && !hasAlt && !hasShift && !hasWin && !isFunctionKey)
            {
                error = "快捷键需包含修饰键（如 Ctrl+Alt+1），或单独使用 F1-F12。";
                return false;
            }

            uint mod = 0;
            var sb = new StringBuilder();
            if (hasCtrl)
            {
                mod |= NativeMethods.MOD_CONTROL;
                AppendPart(sb, "Ctrl");
            }
            if (hasAlt)
            {
                mod |= NativeMethods.MOD_ALT;
                AppendPart(sb, "Alt");
            }
            if (hasShift)
            {
                mod |= NativeMethods.MOD_SHIFT;
                AppendPart(sb, "Shift");
            }
            if (hasWin)
            {
                mod |= NativeMethods.MOD_WIN;
                AppendPart(sb, "Win");
            }
            AppendPart(sb, keyDisplay);

            string built = sb.ToString();
            if (IsReservedSystemHotkey(mod, vk))
            {
                error = "不支持绑定系统常用快捷键「" + built + "」，请改用其他组合（如 Ctrl+Alt+1）。";
                return false;
            }

            modifiers = mod;
            virtualKey = vk;
            canonical = built;
            return true;
        }

        public static bool TryFromKeyEvent(Keys keyData, out string canonical, out string error)
        {
            return TryFromKeyEvent(keyData, false, out canonical, out error);
        }

        public static bool TryFromKeyEvent(Keys keyData, bool winDown, out string canonical, out string error)
        {
            canonical = null;
            error = null;

            Keys modifiers = keyData & Keys.Modifiers;
            Keys keyCode = keyData & Keys.KeyCode;

            if (keyCode == Keys.None
                || keyCode == Keys.ControlKey || keyCode == Keys.LControlKey || keyCode == Keys.RControlKey
                || keyCode == Keys.Menu || keyCode == Keys.LMenu || keyCode == Keys.RMenu
                || keyCode == Keys.ShiftKey || keyCode == Keys.LShiftKey || keyCode == Keys.RShiftKey
                || keyCode == Keys.LWin || keyCode == Keys.RWin)
            {
                error = "请继续按下主键。";
                return false;
            }

            string keyName;
            if (!TryKeysToToken(keyCode, out keyName))
            {
                error = "不支持该按键作为快捷键：" + keyCode;
                return false;
            }

            var sb = new StringBuilder();
            if ((modifiers & Keys.Control) == Keys.Control) AppendPart(sb, "Ctrl");
            if ((modifiers & Keys.Alt) == Keys.Alt) AppendPart(sb, "Alt");
            if ((modifiers & Keys.Shift) == Keys.Shift) AppendPart(sb, "Shift");
            if (winDown) AppendPart(sb, "Win");
            AppendPart(sb, keyName);

            uint mod;
            uint vk;
            return TryParse(sb.ToString(), out mod, out vk, out canonical, out error);
        }

        private static bool IsReservedSystemHotkey(uint modifiers, uint vk)
        {
            if (modifiers == NativeMethods.MOD_CONTROL)
                return vk == 'V' || vk == 'C' || vk == 'X' || vk == 'A' || vk == 'Z';
            if (modifiers == NativeMethods.MOD_ALT)
                return vk == 0x73; // F4
            return false;
        }

        public static string ValidateUnique(IList<string> hotkeys, out int duplicateIndex)
        {
            duplicateIndex = -1;
            if (hotkeys == null)
                return null;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < hotkeys.Count; i++)
            {
                string norm = Normalize(hotkeys[i]);
                if (string.IsNullOrEmpty(norm))
                    continue;
                if (!seen.Add(norm))
                {
                    duplicateIndex = i;
                    return "快捷键「" + norm + "」与其他凭据重复。";
                }
            }
            return null;
        }

        private static bool TryMapKeyToken(
            string token,
            out uint vk,
            out string display,
            out bool isFunctionKey,
            out string error)
        {
            vk = 0;
            display = null;
            isFunctionKey = false;
            error = null;

            string upper = token.Trim().ToUpperInvariant();
            if (upper.Length == 1)
            {
                char ch = upper[0];
                if (ch >= 'A' && ch <= 'Z')
                {
                    vk = (uint)ch;
                    display = upper;
                    return true;
                }
                if (ch >= '0' && ch <= '9')
                {
                    vk = (uint)ch;
                    display = upper;
                    return true;
                }
            }

            if (upper.Length == 2 && upper[0] == 'D' && upper[1] >= '0' && upper[1] <= '9')
            {
                vk = (uint)upper[1];
                display = upper[1].ToString();
                return true;
            }

            if (upper.StartsWith("F", StringComparison.Ordinal) && upper.Length >= 2)
            {
                int fNum;
                if (int.TryParse(upper.Substring(1), out fNum) && fNum >= 1 && fNum <= 12)
                {
                    vk = (uint)(0x70 + (fNum - 1));
                    display = "F" + fNum;
                    isFunctionKey = true;
                    return true;
                }
            }

            if (upper.StartsWith("NUMPAD", StringComparison.Ordinal) && upper.Length == 7)
            {
                char d = upper[6];
                if (d >= '0' && d <= '9')
                {
                    vk = (uint)(0x60 + (d - '0'));
                    display = "NumPad" + d;
                    return true;
                }
            }

            switch (upper)
            {
                case "INSERT":
                    vk = (uint)Keys.Insert;
                    display = "Insert";
                    return true;
                case "HOME":
                    vk = (uint)Keys.Home;
                    display = "Home";
                    return true;
                case "END":
                    vk = (uint)Keys.End;
                    display = "End";
                    return true;
                case "PAGEUP":
                    vk = (uint)Keys.PageUp;
                    display = "PageUp";
                    return true;
                case "PAGEDOWN":
                    vk = (uint)Keys.PageDown;
                    display = "PageDown";
                    return true;
                case "UP":
                    vk = (uint)Keys.Up;
                    display = "Up";
                    return true;
                case "DOWN":
                    vk = (uint)Keys.Down;
                    display = "Down";
                    return true;
                case "LEFT":
                    vk = (uint)Keys.Left;
                    display = "Left";
                    return true;
                case "RIGHT":
                    vk = (uint)Keys.Right;
                    display = "Right";
                    return true;
                case "-":
                case "OEMMINUS":
                    vk = (uint)Keys.OemMinus;
                    display = "-";
                    return true;
                case "=":
                case "OEMPLUS":
                    vk = (uint)Keys.Oemplus;
                    display = "=";
                    return true;
                case ";":
                case "OEM1":
                    vk = (uint)Keys.Oem1;
                    display = ";";
                    return true;
                case ",":
                case "OEMCOMMA":
                    vk = (uint)Keys.Oemcomma;
                    display = ",";
                    return true;
                case ".":
                case "OEMPERIOD":
                    vk = (uint)Keys.OemPeriod;
                    display = ".";
                    return true;
                case "/":
                case "OEM2":
                    vk = (uint)Keys.Oem2;
                    display = "/";
                    return true;
                case "`":
                case "OEM3":
                case "OEMTILDE":
                    vk = (uint)Keys.Oemtilde;
                    display = "`";
                    return true;
            }

            error = "不支持该快捷键主键：" + token;
            return false;
        }

        private static bool TryKeysToToken(Keys keyCode, out string token)
        {
            token = null;
            if (keyCode >= Keys.A && keyCode <= Keys.Z)
            {
                token = ((char)('A' + (keyCode - Keys.A))).ToString();
                return true;
            }
            if (keyCode >= Keys.D0 && keyCode <= Keys.D9)
            {
                token = ((char)('0' + (keyCode - Keys.D0))).ToString();
                return true;
            }
            if (keyCode >= Keys.NumPad0 && keyCode <= Keys.NumPad9)
            {
                token = "NumPad" + (keyCode - Keys.NumPad0);
                return true;
            }
            if (keyCode >= Keys.F1 && keyCode <= Keys.F12)
            {
                token = "F" + ((keyCode - Keys.F1) + 1);
                return true;
            }
            switch (keyCode)
            {
                case Keys.Insert: token = "Insert"; return true;
                case Keys.Home: token = "Home"; return true;
                case Keys.End: token = "End"; return true;
                case Keys.PageUp: token = "PageUp"; return true;
                case Keys.PageDown: token = "PageDown"; return true;
                case Keys.Up: token = "Up"; return true;
                case Keys.Down: token = "Down"; return true;
                case Keys.Left: token = "Left"; return true;
                case Keys.Right: token = "Right"; return true;
                case Keys.OemMinus: token = "-"; return true;
                case Keys.Oemplus: token = "="; return true;
                case Keys.Oem1: token = ";"; return true;
                case Keys.Oemcomma: token = ","; return true;
                case Keys.OemPeriod: token = "."; return true;
                case Keys.Oem2: token = "/"; return true;
                case Keys.Oemtilde: token = "`"; return true;
                default: return false;
            }
        }

        private static void AppendPart(StringBuilder sb, string part)
        {
            if (sb.Length > 0)
                sb.Append('+');
            sb.Append(part);
        }
    }
}
