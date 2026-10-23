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

        /// <summary>设置保存后触发，供文件树等界面即时刷新。</summary>
        public event EventHandler Changed;

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
                    if (d == null) return;
                    ConfirmBeforeHide = d.ConfirmBeforeHide;
                    ShowHiddenFiles = d.ShowHiddenFiles;
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("读取设置失败，使用默认值：" + _filePath, ex);
            }
        }

        /// <summary>保存设置；返回 false 表示写入失败（内存中的值仍然生效）。</summary>
        public bool Save()
        {
            var success = true;
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
            catch (Exception ex)
            {
                success = false;
                LogService.Error("保存设置失败：" + _filePath, ex);
            }

            // 即使落盘失败，内存中的值也已变化，界面应同步
            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogService.Warn("设置变更通知处理失败", ex);
            }

            return success;
        }
    }
}
