using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using InspectionWidget.Config;
using InspectionWidget.Native;

namespace InspectionWidget.Credentials
{
    public interface IHotkeyRegistrar
    {
        bool Register(IntPtr hwnd, int id, uint modifiers, uint virtualKey, out int win32Error);
        bool Unregister(IntPtr hwnd, int id);
    }

    public sealed class Win32HotkeyRegistrar : IHotkeyRegistrar
    {
        public bool Register(IntPtr hwnd, int id, uint modifiers, uint virtualKey, out int win32Error)
        {
            win32Error = 0;
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                win32Error = 50;
                return false;
            }
            if (NativeMethods.RegisterHotKey(hwnd, id, modifiers | NativeMethods.MOD_NOREPEAT, virtualKey))
                return true;
            win32Error = Marshal.GetLastWin32Error();
            return false;
        }

        public bool Unregister(IntPtr hwnd, int id)
        {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                return true;
            return NativeMethods.UnregisterHotKey(hwnd, id);
        }
    }

    public sealed class HotkeyBindingFailure
    {
        public int Index;
        public string Title;
        public string Hotkey;
        public string Error;
    }

    /// <summary>管理主窗体的全局凭据快捷键注册与消息分发。</summary>
    public sealed class GlobalHotkeyManager : IDisposable
    {
        private const int BaseHotkeyId = 0xB000;

        private readonly IHotkeyRegistrar _registrar;
        private readonly Dictionary<int, int> _idToCredentialIndex = new Dictionary<int, int>();
        private IntPtr _boundHwnd;
        private bool _disposed;

        public GlobalHotkeyManager()
            : this(new Win32HotkeyRegistrar())
        {
        }

        public GlobalHotkeyManager(IHotkeyRegistrar registrar)
        {
            _registrar = registrar ?? new Win32HotkeyRegistrar();
        }

        public List<HotkeyBindingFailure> Bind(IntPtr hwnd, IList<CredentialEntry> credentials)
        {
            var failures = new List<HotkeyBindingFailure>();
            UnbindAll();
            if (_disposed || hwnd == IntPtr.Zero || credentials == null)
                return failures;

            _boundHwnd = hwnd;
            for (int i = 0; i < credentials.Count; i++)
            {
                CredentialEntry entry = credentials[i];
                if (entry == null || entry.NeedsReset || string.IsNullOrWhiteSpace(entry.Hotkey))
                    continue;

                uint modifiers;
                uint vk;
                string canonical;
                string parseErr;
                if (!HotkeySpec.TryParse(entry.Hotkey, out modifiers, out vk, out canonical, out parseErr))
                {
                    failures.Add(new HotkeyBindingFailure
                    {
                        Index = i,
                        Title = entry.Title,
                        Hotkey = entry.Hotkey,
                        Error = parseErr
                    });
                    continue;
                }

                int id = BaseHotkeyId + i;
                int win32Err;
                if (_registrar.Register(hwnd, id, modifiers, vk, out win32Err))
                {
                    _idToCredentialIndex[id] = i;
                }
                else
                {
                    failures.Add(new HotkeyBindingFailure
                    {
                        Index = i,
                        Title = entry.Title,
                        Hotkey = canonical,
                        Error = "快捷键「" + canonical + "」可能已被其他程序占用（Win32=" + win32Err + "）"
                    });
                }
            }
            return failures;
        }

        public bool TryResolveHotkey(int id, out int credentialIndex)
        {
            return _idToCredentialIndex.TryGetValue(id, out credentialIndex);
        }

        public void UnbindAll()
        {
            if (_boundHwnd != IntPtr.Zero)
            {
                foreach (int id in _idToCredentialIndex.Keys)
                {
                    try
                    {
                        _registrar.Unregister(_boundHwnd, id);
                    }
                    catch
                    {
                    }
                }
            }
            _idToCredentialIndex.Clear();
            _boundHwnd = IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            UnbindAll();
        }
    }
}
