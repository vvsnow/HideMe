using System;
using PropertyChanged;

namespace HideMe.Models
{
    /// <summary>
    /// 隐藏列表页的一行：一条被隐藏的文件/文件夹记录。
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class HiddenItem
    {
        public string OriginalPath { get; set; }   // 原路径（恢复目标）
        public string VaultPath { get; set; }      // 存放的 \$Recycle.Bin\<SID> 目录
        public string StreamId { get; set; }       // 数据/元数据流 id（HideMe_<id> / HideMeMeta_<id>）
        public string Kind { get; set; }           // "File" | "Folder"
        public DateTime HiddenAt { get; set; }     // 隐藏时间
    }
}
