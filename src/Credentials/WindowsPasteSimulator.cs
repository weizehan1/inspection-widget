using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using InspectionWidget.Native;

namespace InspectionWidget.Credentials
{
    public sealed class WindowsPasteSimulator : IPasteSimulator
    {
        public bool TryPaste(out string error)
        {
            error = null;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                error = "仅支持在 Windows 上自动粘贴。";
                return false;
            }

            try
            {
                WaitOrReleaseModifiers();

                var inputs = new List<NativeMethods.INPUT>(4);
                inputs.Add(KeyInput(NativeMethods.VK_CONTROL, false));
                inputs.Add(KeyInput(NativeMethods.VK_V, false));
                inputs.Add(KeyInput(NativeMethods.VK_V, true));
                inputs.Add(KeyInput(NativeMethods.VK_CONTROL, true));

                NativeMethods.INPUT[] arr = inputs.ToArray();
                uint sent = NativeMethods.SendInput(
                    (uint)arr.Length,
                    arr,
                    Marshal.SizeOf(typeof(NativeMethods.INPUT)));
                if (sent != arr.Length)
                {
                    int errCode = Marshal.GetLastWin32Error();
                    error = "发送粘贴按键失败（Win32=" + errCode + "）。";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = "发送粘贴按键失败：" + ex.Message;
                return false;
            }
        }

        private static void WaitOrReleaseModifiers()
        {
            ushort[] modifiers =
            {
                NativeMethods.VK_LCONTROL,
                NativeMethods.VK_RCONTROL,
                NativeMethods.VK_LMENU,
                NativeMethods.VK_RMENU,
                NativeMethods.VK_LSHIFT,
                NativeMethods.VK_RSHIFT,
                NativeMethods.VK_LWIN,
                NativeMethods.VK_RWIN
            };

            // 先给用户指尖抬起留极短缓冲，避免快捷键修饰键残留粘连
            for (int step = 0; step < 10; step++)
            {
                if (!AnyKeyDown(modifiers))
                    return;
                Thread.Sleep(15);
            }

            var releases = new List<NativeMethods.INPUT>();
            bool altHeld = IsKeyDown(NativeMethods.VK_LMENU) || IsKeyDown(NativeMethods.VK_RMENU);
            if (altHeld)
            {
                // 注入掩码键打断单独 Alt 抬起触发目标窗口顶部菜单栏的默认行为
                releases.Add(KeyInput(0xFF, false));
                releases.Add(KeyInput(0xFF, true));
            }
            for (int i = 0; i < modifiers.Length; i++)
            {
                if (IsKeyDown(modifiers[i]))
                    releases.Add(KeyInput(modifiers[i], true));
            }
            if (releases.Count > 0)
            {
                NativeMethods.INPUT[] arr = releases.ToArray();
                NativeMethods.SendInput(
                    (uint)arr.Length,
                    arr,
                    Marshal.SizeOf(typeof(NativeMethods.INPUT)));
                Thread.Sleep(10);
            }
        }

        private static bool AnyKeyDown(ushort[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                if (IsKeyDown(keys[i]))
                    return true;
            }
            return false;
        }

        private static bool IsKeyDown(ushort vk)
        {
            return (NativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0;
        }

        private static bool IsExtendedKey(ushort vk)
        {
            return vk == NativeMethods.VK_RCONTROL
                || vk == NativeMethods.VK_RMENU
                || vk == NativeMethods.VK_LWIN
                || vk == NativeMethods.VK_RWIN;
        }

        private static NativeMethods.INPUT KeyInput(ushort vk, bool keyUp)
        {
            ushort scan = 0;
            try
            {
                scan = (ushort)NativeMethods.MapVirtualKey(vk, NativeMethods.MAPVK_VK_TO_VSC);
            }
            catch
            {
            }
            uint flags = (keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0u)
                | (IsExtendedKey(vk) ? NativeMethods.KEYEVENTF_EXTENDEDKEY : 0u);
            var input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.u.ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                wScan = scan,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            };
            return input;
        }
    }
}
