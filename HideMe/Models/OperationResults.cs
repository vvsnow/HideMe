using System;
using System.Collections.Generic;

namespace HideMe.Models
{
    /// <summary>
    /// 长时间操作的进度信息。Total 为 0 表示进度不确定。
    /// </summary>
    public sealed class ProgressInfo
    {
        public string Stage { get; set; }
        public long Processed { get; set; }
        public long Total { get; set; }
    }

    /// <summary>
    /// 隐藏操作的结果：内容实际落在哪个保险库、是否使用了系统盘回退。
    /// </summary>
    public sealed class HideResult
    {
        public string VaultPath { get; set; }
        public bool UsedFallback { get; set; }
    }

    /// <summary>
    /// 恢复操作的结果：内容已恢复，但保险库副本清理可能失败。
    /// </summary>
    public sealed class ShowResult
    {
        public string RestoredPath { get; set; }
        public bool VaultDataDeleted { get; set; }
        public bool VaultMetaDeleted { get; set; }
        public string CleanupError { get; set; }
    }

    /// <summary>
    /// 扫描结果：正常条目 + 被跳过的损坏/不可读条目数量。
    /// </summary>
    public sealed class ScanResult
    {
        public List<HiddenItem> Items { get; } = new List<HiddenItem>();
        public int Skipped { get; set; }
    }

    /// <summary>
    /// 恢复目标已存在时抛出，由 UI 询问用户是否覆盖。
    /// </summary>
    public sealed class TargetExistsException : Exception
    {
        public TargetExistsException(string path)
            : base("目标已存在：" + path)
        {
            TargetPath = path;
        }

        public string TargetPath { get; private set; }
    }
}
