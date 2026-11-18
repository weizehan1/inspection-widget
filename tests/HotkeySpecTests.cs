using System;
using System.Collections.Generic;
using System.Windows.Forms;
using InspectionWidget.Config;
using InspectionWidget.Credentials;

namespace InspectionWidget.Tests
{
    public static class HotkeySpecTests
    {
        private sealed class FakeHotkeyRegistrar : IHotkeyRegistrar
        {
            public readonly Dictionary<int, string> Registered = new Dictionary<int, string>();
            public readonly HashSet<uint> OccupiedVks = new HashSet<uint>();
            public int UnregisterCalls;

            public bool Register(IntPtr hwnd, int id, uint modifiers, uint virtualKey, out int win32Error)
            {
                if (OccupiedVks.Contains(virtualKey))
                {
                    win32Error = 1409;
                    return false;
                }
                win32Error = 0;
                Registered[id] = modifiers + ":" + virtualKey;
                return true;
            }

            public bool Unregister(IntPtr hwnd, int id)
            {
                UnregisterCalls++;
                Registered.Remove(id);
                return true;
            }
        }

        public static void RunAll()
        {
            TestRunner.Run("快捷键：规范化、修饰键顺序与主键解析", () =>
            {
                uint mod;
                uint vk;
                string canonical;
                string error;

                Assert.True(HotkeySpec.TryParse("alt+control+d1", out mod, out vk, out canonical, out error), error);
                Assert.Equal("Ctrl+Alt+1", canonical);
                Assert.Equal((uint)'1', vk);

                Assert.True(HotkeySpec.TryParse("f9", out mod, out vk, out canonical, out error), error);
                Assert.Equal("F9", canonical);
                Assert.Equal(0u, mod);

                Assert.Null(HotkeySpec.Validate(null));
                Assert.Null(HotkeySpec.Validate("   "));
                Assert.NotNull(HotkeySpec.Validate("A"));
                Assert.NotNull(HotkeySpec.Validate("Ctrl+"));
                Assert.NotNull(HotkeySpec.Validate("Ctrl+Space"));
            });

            TestRunner.Run("快捷键：KeyEvent 转换与重复检测", () =>
            {
                string canonical;
                string error;
                Assert.True(HotkeySpec.TryFromKeyEvent(Keys.Control | Keys.Alt | Keys.D2, out canonical, out error), error);
                Assert.Equal("Ctrl+Alt+2", canonical);

                Assert.False(HotkeySpec.TryFromKeyEvent(Keys.Control | Keys.ControlKey, out canonical, out error));

                int dupIndex;
                string dupErr = HotkeySpec.ValidateUnique(
                    new[] { "Ctrl+Alt+1", "", "alt+ctrl+d1" },
                    out dupIndex);
                Assert.NotNull(dupErr);
                Assert.Equal(2, dupIndex);
            });

            TestRunner.Run("全局快捷键管理：绑定、冲突报告、消息解析与解绑", () =>
            {
                var registrar = new FakeHotkeyRegistrar();
                registrar.OccupiedVks.Add((uint)'2');

                var manager = new GlobalHotkeyManager(registrar);
                var creds = new List<CredentialEntry>
                {
                    new CredentialEntry { Title = "运维", Hotkey = "Ctrl+Alt+1" },
                    new CredentialEntry { Title = "堡垒机", Hotkey = "Ctrl+Alt+2" },
                    new CredentialEntry { Title = "无快捷键", Hotkey = null }
                };

                List<HotkeyBindingFailure> failures = manager.Bind(new IntPtr(1234), creds);
                Assert.Equal(1, failures.Count);
                Assert.Equal(1, failures[0].Index);
                Assert.Equal(1, registrar.Registered.Count);

                int resolvedId = -1;
                foreach (int id in registrar.Registered.Keys)
                {
                    resolvedId = id;
                    break;
                }
                int credIdx;
                Assert.True(manager.TryResolveHotkey(resolvedId, out credIdx));
                Assert.Equal(0, credIdx);

                manager.UnbindAll();
                Assert.Equal(0, registrar.Registered.Count);
                Assert.True(registrar.UnregisterCalls >= 1);
            });

            TestRunner.Run("凭据快捷键与 U盘目录：配置克隆/导出导入与保存协调器校验", () =>
            {
                var source = AppConfig.CreateDefault();
                source.UsbSyncRootPath = "D:\\巡检";
                source.Credentials.Add(new CredentialEntry
                {
                    Title = "账号A",
                    EncryptedSecret = "QUJD",
                    Hotkey = "Ctrl+Alt+1"
                });

                string fullJson = ConfigTransfer.BuildExportJson(source, ConfigJsonReader.CredentialModeDpapi);
                ConfigImportDocument doc;
                string error;
                Assert.True(ConfigJsonReader.TryParse(fullJson, out doc, out error), error);
                Assert.Equal("D:\\巡检", doc.Config.UsbSyncRootPath);
                Assert.Equal("Ctrl+Alt+1", doc.Config.Credentials[0].Hotkey);

                var inputs = new List<CredentialRowCollector.RowInput>
                {
                    new CredentialRowCollector.RowInput { Title = "A", IsNew = true, NewPlainText = "123", Hotkey = "Ctrl+Alt+1" },
                    new CredentialRowCollector.RowInput { Title = "B", IsNew = true, NewPlainText = "456", Hotkey = "alt+ctrl+1" }
                };
                SettingsSaveCoordinator.Outcome dupOutcome = SettingsSaveCoordinator.Build(
                    source,
                    new List<string> { "Dev1" },
                    inputs,
                    new List<QuickLinkEntry>(),
                    "E:\\Work",
                    "D:\\巡检",
                    "巡检-yyyy-MM-dd",
                    true,
                    100,
                    CaptureMode.Auto,
                    false,
                    true,
                    new FakeProtector());
                Assert.False(dupOutcome.Saved);
                Assert.Equal(1, dupOutcome.ErrorGroup);
                Assert.True(dupOutcome.CredentialErrorOnHotkey);
                Assert.Equal(1, dupOutcome.CredentialErrorIndex);
            });
        }

        private sealed class FakeProtector : ICredentialProtector
        {
            public string Protect(string plainText) { return "ENC:" + plainText; }
            public string Unprotect(string cipherText) { return cipherText; }
        }
    }
}
