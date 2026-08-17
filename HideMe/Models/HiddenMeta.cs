using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace HideMe.Models
{
    /// <summary>
    /// 隐藏项的元数据，序列化后写入保险库目录的 HideMeMeta_* 流。
    /// </summary>
    [DataContract]
    public class HiddenMeta
    {
        [DataMember] public string Kind { get; set; }          // "File" | "Folder"
        [DataMember] public string OriginalPath { get; set; }  // 被隐藏的原始完整路径
        [DataMember] public DateTime HiddenAt { get; set; }    // 隐藏时间

        public static string ToJson(HiddenMeta m)
        {
            using (var ms = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(HiddenMeta)).WriteObject(ms, m);
                return System.Text.Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static HiddenMeta FromJson(string json)
        {
            using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json)))
                return (HiddenMeta)new DataContractJsonSerializer(typeof(HiddenMeta)).ReadObject(ms);
        }
    }
}
