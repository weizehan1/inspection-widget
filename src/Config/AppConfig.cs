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

        /// <summary>截图（设备）分区是否折叠（v1.0.3），默认 false；与凭据区折叠互不影响。</summary>
        [DataMember]
        public bool IsDeviceCollapsed { get; set; }

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

        /// <summary>界面缩放百分比（v1.0.1）：合法档位见 UiScale，默认 100；非法值在加载时归一。</summary>
        [DataMember]
        public int UiScalePercent { get; set; }

        /// <summary>截图日期文件夹名模式（v1.0.2 引入）：默认见 FolderNamePattern.Default（v1.0.3 起为 巡检-yyyy-MM-dd）；缺字段/空值在加载时自愈为默认，已保存值不迁移。</summary>
        [DataMember]
        public string FolderNamePattern { get; set; }

        /// <summary>托盘气泡提示是否已展示过（v1.0.2）：首次最小化到托盘提示一次后置 true，默认 false。</summary>
        [DataMember]
        public bool TrayHintShown { get; set; }

        // DataContract 反序列化不走构造函数：先把字段设成默认值，JSON 里有的字段再覆盖。
        // 这样缺 AutoClearClipboard 字段的老/手改配置加载后仍是默认 true，列表也不会是 null。
        [OnDeserializing]
        private void OnDeserializing(StreamingContext context)
        {
            AutoClearClipboard = true;
            IsCredentialCollapsed = false;
            IsDeviceCollapsed = false;
            CaptureMode = CaptureMode.Auto;
            UiScalePercent = UiScale.DefaultPercent;
            FolderNamePattern = global::InspectionWidget.Config.FolderNamePattern.Default;
            TrayHintShown = false;
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
                IsDeviceCollapsed = false,
                AutoClearClipboard = true,
                CaptureMode = CaptureMode.Auto,
                WindowLeft = null,
                WindowTop = null,
                LastDevice = null,
                UiScalePercent = UiScale.DefaultPercent,
                FolderNamePattern = global::InspectionWidget.Config.FolderNamePattern.Default,
                TrayHintShown = false
            };
        }
    }
}
