using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace HideMe.Services
{
    [DataContract]
    public class SettingsData
    {
        [DataMember] public bool ConfirmBeforeHide { get; set; }
        [DataMember] public bool ShowHiddenFiles { get; set; }
    }

    /// <summary>
    /// 简单设置持久化（%LOCALAPPDATA%\HideMe\settings.json），失败回默认值。
    /// </summary>
    public class SettingsService
    {
        private readonly string _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HideMe", "settings.json");

        public bool ConfirmBeforeHide { get; set; } = true;
        public bool ShowHiddenFiles { get; set; }

        public SettingsService()
        {
            Load();   // 单例创建时即读取已保存设置
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_filePath)) return;
                using (var fs = File.OpenRead(_filePath))
                {
                    var d = (SettingsData)new DataContractJsonSerializer(typeof(SettingsData)).ReadObject(fs);
                    ConfirmBeforeHide = d.ConfirmBeforeHide;
                    ShowHiddenFiles = d.ShowHiddenFiles;
                }
            }
            catch { /* 损坏或缺失 → 保持默认 */ }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath));
                using (var fs = File.Create(_filePath))
                {
                    new DataContractJsonSerializer(typeof(SettingsData)).WriteObject(fs, new SettingsData
                    {
                        ConfirmBeforeHide = ConfirmBeforeHide,
                        ShowHiddenFiles = ShowHiddenFiles,
                    });
                }
            }
            catch { /* 写失败不致命，下次再试 */ }
        }
    }
}
