using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace HideMe.Services
{
    [DataContract]
    public sealed class IndexEntry
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Kind { get; set; }
        [DataMember] public string OriginalPath { get; set; }
        [DataMember] public string VaultPath { get; set; }
        [DataMember] public DateTime HiddenAt { get; set; }
        [DataMember] public string Sha256 { get; set; }
    }

    [DataContract]
    public sealed class IndexFile
    {
        [DataMember] public List<IndexEntry> Entries { get; set; }
    }

    /// <summary>
    /// 隐藏项索引：%LOCALAPPDATA%\HideMe\index.json。
    /// 作为元数据流的冗余；索引丢失不影响扫描（仍可从流恢复），
    /// 元数据流丢失时可借助索引恢复条目。
    /// </summary>
    public static class HideIndex
    {
        private static readonly object SyncRoot = new object();
        private static readonly string IndexPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HideMe", "index.json");

        public static List<IndexEntry> GetAll()
        {
            lock (SyncRoot)
            {
                return LoadCore();
            }
        }

        public static IndexEntry Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (SyncRoot)
            {
                foreach (var entry in LoadCore())
                {
                    if (string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                        return entry;
                }
                return null;
            }
        }

        public static void Upsert(IndexEntry entry)
        {
            if (entry == null || string.IsNullOrEmpty(entry.Id)) return;
            lock (SyncRoot)
            {
                var entries = LoadCore();
                UpsertCore(entries, entry);
                SaveCore(entries);
            }
        }

        public static void UpsertRange(IEnumerable<IndexEntry> items)
        {
            if (items == null) return;
            lock (SyncRoot)
            {
                var entries = LoadCore();
                foreach (var item in items)
                {
                    if (item != null && !string.IsNullOrEmpty(item.Id))
                        UpsertCore(entries, item);
                }
                SaveCore(entries);
            }
        }

        public static void Remove(string id)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (SyncRoot)
            {
                var entries = LoadCore();
                entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
                SaveCore(entries);
            }
        }

        private static void UpsertCore(List<IndexEntry> entries, IndexEntry entry)
        {
            var index = entries.FindIndex(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) entries[index] = entry;
            else entries.Add(entry);
        }

        private static List<IndexEntry> LoadCore()
        {
            try
            {
                if (!File.Exists(IndexPath)) return new List<IndexEntry>();
                using (var stream = File.OpenRead(IndexPath))
                {
                    var file = (IndexFile)new DataContractJsonSerializer(typeof(IndexFile)).ReadObject(stream);
                    return file != null && file.Entries != null
                        ? file.Entries
                        : new List<IndexEntry>();
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("读取隐藏索引失败，按空索引处理：" + IndexPath, ex);
                return new List<IndexEntry>();
            }
        }

        private static void SaveCore(List<IndexEntry> entries)
        {
            try
            {
                var directory = Path.GetDirectoryName(IndexPath);
                if (!string.IsNullOrEmpty(directory))
                    System.IO.Directory.CreateDirectory(directory);

                var temp = IndexPath + ".tmp";
                using (var stream = File.Create(temp))
                {
                    new DataContractJsonSerializer(typeof(IndexFile)).WriteObject(stream,
                        new IndexFile { Entries = entries });
                }

                if (File.Exists(IndexPath))
                {
                    var backup = IndexPath + ".bak";
                    try
                    {
                        File.Replace(temp, IndexPath, backup, true);
                        try { File.Delete(backup); } catch { }
                    }
                    catch
                    {
                        File.Copy(temp, IndexPath, true);
                        try { File.Delete(temp); } catch { }
                    }
                }
                else
                {
                    File.Move(temp, IndexPath);
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("写入隐藏索引失败：" + IndexPath, ex);
            }
        }
    }
}
