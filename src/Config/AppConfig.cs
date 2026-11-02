using System.Collections.Generic;
using System.Runtime.Serialization;

namespace InspectionWidget.Config
{
    /// <summary>截图模式：自动 / 强制 PrintWindow / 强制 BitBlt。</summary>
    public enum CaptureMode
    {
        Auto,
        PrintWindowOnly,
        BitBltOnly
    }

    /// <summary>一条凭据：名称 + DPAPI 密文（Base64）。明文永不落盘。</summary>
    [DataContract]
    public sealed class CredentialEntry
    {
        [DataMember]
        public string Title { get; set; }

        [DataMember]
        public string EncryptedSecret { get; set; }

        /// <summary>运行时标记：密文解不开时置 true，仅界面提示用，不落盘。</summary>
        [IgnoreDataMember]
        public bool NeedsReset { get; set; }
    }

    /// <summary>应用配置（config.json 的 DataContract 模型）。</summary>
    [DataContract]
    public sealed class AppConfig
    {
        [DataMember]
        public string RootPath { get; set; }

        [DataMember]
        public List<string> Devices { get; set; }

        [DataMember]
        public List<CredentialEntry> Credentials { get; set; }

        /// <summary>凭据区分区是否折叠，默认 false。</summary>
        [DataMember]
        public bool IsCredentialCollapsed { get; set; }

        /// <summary>复制凭据后 30 秒自动清空剪贴板，默认 true（缺字段时靠 OnDeserializing 兜底）。</summary>
        [DataMember]
        public bool AutoClearClipboard { get; set; }

        [DataMember]
        public CaptureMode CaptureMode { get; set; }

        [DataMember]
        public int? WindowLeft { get; set; }

        [DataMember]
        public int? WindowTop { get; set; }

        [DataMember]
        public string LastDevice { get; set; }

        // DataContract 反序列化不走构造函数：先把字段设成默认值，JSON 里有的字段再覆盖。
        // 这样缺 AutoClearClipboard 字段的老/手改配置加载后仍是默认 true，列表也不会是 null。
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            AutoClearClipboard = true;
            IsCredentialCollapsed = false;
            CaptureMode = CaptureMode.Auto;
            Devices = new List<string>();
            Credentials = new List<CredentialEntry>();
        }

        /// <summary>默认配置：两条示例设备，其余按约定默认值。</summary>
        public static AppConfig CreateDefault()
        {
            return new AppConfig
            {
                RootPath = null,
                Devices = new List<string> { "设备1", "设备2" },
                Credentials = new List<CredentialEntry>(),
                IsCredentialCollapsed = false,
                AutoClearClipboard = true,
                CaptureMode = CaptureMode.Auto,
                WindowLeft = null,
                WindowTop = null,
                LastDevice = null
            };
        }
    }
}
