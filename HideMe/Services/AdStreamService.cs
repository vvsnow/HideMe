using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using HideMe.Models;

namespace HideMe.Services
{
    /// <summary>
    /// NTFS 替代数据流(ADS) 操作，以及基于"各盘 \$Recycle.Bin\&lt;当前SID&gt;"保险库的隐藏/显示。
    /// 隐藏 = 把内容写入原文件所在盘的回收站 SID 文件夹的 ADS 流，再永久删除原条目。
    /// </summary>
    public static class AdStreamService
    {
        public const string DataPrefix = "HideMe_";
        public const string MetaPrefix = "HideMeMeta_";

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint FILE_SHARE_DELETE = 0x00000004;
        private const uint CREATE_ALWAYS = 2;
        private const uint OPEN_EXISTING = 3;
        private const uint OPEN_ALWAYS = 4;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;   // 允许打开目录句柄

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFileW(string lpFileName);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_STREAM_DATA
        {
            public long StreamSize;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 296)]
            public string cStreamName;
        }

        private enum STREAM_INFO_LEVELS { FindStreamInfoStandard = 0 }

        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstStreamW(string lpFileName,
            STREAM_INFO_LEVELS infoLevel, ref WIN32_FIND_STREAM_DATA lpFindStreamData, uint dwFlags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextStreamW(IntPtr hFindStream, ref WIN32_FIND_STREAM_DATA lpFindStreamData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindClose(IntPtr hFindFile);

        #region 基础 ADS 操作

        private static string Prefixed(string path)
            => path.StartsWith(@"\\?\") ? path : @"\\?\" + path;

        private static string AdsPath(string path, string stream)
            => Prefixed(Path.GetFullPath(path)) + ":" + stream;

        private static FileStream OpenStream(string path, string stream, FileAccess access, FileMode mode)
        {
            uint accessFlags = (access == FileAccess.Read) ? GENERIC_READ
                : (access == FileAccess.Write) ? GENERIC_WRITE
                : (GENERIC_READ | GENERIC_WRITE);
            uint disposition = (mode == FileMode.Create) ? CREATE_ALWAYS
                : (mode == FileMode.Open) ? OPEN_EXISTING
                : OPEN_ALWAYS;
            var handle = CreateFileW(AdsPath(path, stream), accessFlags,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero, disposition,
                FILE_ATTRIBUTE_NORMAL | FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            return new FileStream(handle, access, 81920, false);
        }

        public static void WriteText(string path, string stream, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            using (var s = OpenStream(path, stream, FileAccess.Write, FileMode.Create))
                s.Write(bytes, 0, bytes.Length);
        }

        public static string ReadText(string path, string stream)
        {
            using (var s = OpenStream(path, stream, FileAccess.Read, FileMode.Open))
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static void CopyFileToStream(string fromPath, string toPath, string toStream)
        {
            using (var src = new FileStream(fromPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var dst = OpenStream(toPath, toStream, FileAccess.Write, FileMode.Create))
                src.CopyTo(dst);
        }

        private static void CopyStreamToFile(string fromPath, string fromStream, string toPath)
        {
            using (var src = OpenStream(fromPath, fromStream, FileAccess.Read, FileMode.Open))
            using (var dst = new FileStream(toPath, FileMode.Create, FileAccess.Write, FileShare.None))
                src.CopyTo(dst);
        }

        /// <summary>规范化 FindFirstStreamW 返回的流名：去掉前导 ':' 和尾部 ':$DATA'。</summary>
        private static string NormalizeStreamName(string raw)
        {
            if (raw.StartsWith(":")) raw = raw.Substring(1);
            const string dataSuffix = ":$DATA";
            if (raw.EndsWith(dataSuffix, StringComparison.OrdinalIgnoreCase))
                raw = raw.Substring(0, raw.Length - dataSuffix.Length);
            return raw;
        }

        /// <summary>path 上是否存在指定流。</summary>
        public static bool StreamExists(string path, string stream)
        {
            var fd = new WIN32_FIND_STREAM_DATA();
            IntPtr h = FindFirstStreamW(Prefixed(Path.GetFullPath(path)),
                STREAM_INFO_LEVELS.FindStreamInfoStandard, ref fd, 0);
            if (h == INVALID_HANDLE_VALUE) return false;   // 无流 / 非 NTFS / 不存在 → false
            try
            {
                do
                {
                    if (string.Equals(NormalizeStreamName(fd.cStreamName), stream, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                while (FindNextStreamW(h, ref fd));
                return false;
            }
            finally { FindClose(h); }
        }

        /// <summary>枚举 path 上的所有流名（规范化，跳过主数据流 $DATA）。</summary>
        public static List<string> EnumerateStreams(string path)
        {
            var list = new List<string>();
            var fd = new WIN32_FIND_STREAM_DATA();
            IntPtr h = FindFirstStreamW(Prefixed(Path.GetFullPath(path)),
                STREAM_INFO_LEVELS.FindStreamInfoStandard, ref fd, 0);
            if (h == INVALID_HANDLE_VALUE) return list;
            try
            {
                do
                {
                    var name = NormalizeStreamName(fd.cStreamName);
                    if (name.Length > 0 && !string.Equals(name, "$DATA", StringComparison.OrdinalIgnoreCase))
                        list.Add(name);
                }
                while (FindNextStreamW(h, ref fd));
            }
            finally { FindClose(h); }
            return list;
        }

        public static void DeleteStream(string path, string stream)
        {
            DeleteFileW(AdsPath(path, stream));
        }

        #endregion

        #region 保险库定位

        private static string CurrentSid => WindowsIdentity.GetCurrent().User?.Value ?? "";

        /// <summary>
        /// 返回 path 所在盘 \$Recycle.Bin\&lt;当前SID&gt;。
        /// 不存在则尝试创建；创建失败（无权限）则回退到系统盘的 SID 文件夹。
        /// </summary>
        public static string GetVaultDirectory(string path)
        {
            var driveRoot = Path.GetPathRoot(Path.GetFullPath(path));
            var primary = Path.Combine(driveRoot, "$Recycle.Bin", CurrentSid);
            if (Directory.Exists(primary)) return primary;
            try
            {
                Directory.CreateDirectory(primary);
                return primary;
            }
            catch
            {
                var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
                var fallback = Path.Combine(systemDrive, "$Recycle.Bin", CurrentSid);
                try { Directory.CreateDirectory(fallback); } catch { }
                return fallback;
            }
        }

        #endregion

        #region 隐藏 / 显示

        public static void HideFile(string path)
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException("文件不存在", full);
            var vault = GetVaultDirectory(full);
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            var dataStream = DataPrefix + id;
            var metaStream = MetaPrefix + id;

            try
            {
                // 1) 内容 -> 数据流；2) 元数据；3) 永久删除原条目
                CopyFileToStream(full, vault, dataStream);
                WriteText(vault, metaStream, HiddenMeta.ToJson(
                    new HiddenMeta { Kind = "File", OriginalPath = full, HiddenAt = DateTime.Now }));
                try { File.SetAttributes(full, File.GetAttributes(full) & ~FileAttributes.ReadOnly); } catch { }
                File.Delete(full);
            }
            catch
            {
                // 回滚：删除刚写入的流，原文件保持不动
                try { DeleteStream(vault, dataStream); } catch { }
                try { DeleteStream(vault, metaStream); } catch { }
                throw;
            }
        }

        public static void ShowFile(string originalPath, string vaultPath, string streamId)
        {
            var dataStream = DataPrefix + streamId;
            var metaStream = MetaPrefix + streamId;
            var full = Path.GetFullPath(originalPath);
            var dir = Path.GetDirectoryName(full);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            try
            {
                CopyStreamToFile(vaultPath, dataStream, full);
                DeleteStream(vaultPath, dataStream);
                DeleteStream(vaultPath, metaStream);
            }
            catch
            {
                try { if (File.Exists(full)) File.Delete(full); } catch { }   // 恢复失败时清掉半成品
                throw;
            }
        }

        public static void HideFolder(string path)
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("文件夹不存在");
            var vault = GetVaultDirectory(full);
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            var dataStream = DataPrefix + id;
            var metaStream = MetaPrefix + id;
            var tmpZip = Path.Combine(Path.GetTempPath(), "hideme_" + id + ".zip");

            try
            {
                // 打包到临时 zip，再写入数据流
                ZipFile.CreateFromDirectory(full, tmpZip, CompressionLevel.Optimal, includeBaseDirectory: false);
                CopyFileToStream(tmpZip, vault, dataStream);
                WriteText(vault, metaStream, HiddenMeta.ToJson(
                    new HiddenMeta { Kind = "Folder", OriginalPath = full, HiddenAt = DateTime.Now }));
                Directory.Delete(full, recursive: true);
            }
            catch
            {
                try { DeleteStream(vault, dataStream); } catch { }
                try { DeleteStream(vault, metaStream); } catch { }
                throw;
            }
            finally
            {
                try { File.Delete(tmpZip); } catch { }
            }
        }

        public static void ShowFolder(string originalPath, string vaultPath, string streamId)
        {
            var dataStream = DataPrefix + streamId;
            var metaStream = MetaPrefix + streamId;
            var full = Path.GetFullPath(originalPath).TrimEnd('\\', '/');
            var dir = Path.GetDirectoryName(full);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            var tmpZip = Path.Combine(Path.GetTempPath(), "hideme_" + streamId + "_restore.zip");

            try
            {
                CopyStreamToFile(vaultPath, dataStream, tmpZip);
                if (Directory.Exists(full))
                {
                    foreach (var f in Directory.GetFiles(full)) File.Delete(f);
                    foreach (var d in Directory.GetDirectories(full)) Directory.Delete(d, recursive: true);
                }
                else
                {
                    Directory.CreateDirectory(full);
                }
                ZipFile.ExtractToDirectory(tmpZip, full);
                DeleteStream(vaultPath, dataStream);
                DeleteStream(vaultPath, metaStream);
            }
            finally
            {
                try { File.Delete(tmpZip); } catch { }
            }
        }

        /// <summary>扫描每个固定盘的 \$Recycle.Bin\&lt;当前SID&gt;，收集所有隐藏项。</summary>
        public static List<HiddenItem> ScanAll()
        {
            var results = new List<HiddenItem>();
            var sid = CurrentSid;
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                var vault = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
                if (!Directory.Exists(vault)) continue;

                foreach (var stream in EnumerateStreams(vault))
                {
                    if (!stream.StartsWith(MetaPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                    var id = stream.Substring(MetaPrefix.Length);
                    try
                    {
                        var meta = HiddenMeta.FromJson(ReadText(vault, stream));
                        if (string.IsNullOrEmpty(meta.OriginalPath)) continue;
                        results.Add(new HiddenItem
                        {
                            OriginalPath = meta.OriginalPath,
                            VaultPath = vault,
                            StreamId = id,
                            Kind = meta.Kind,
                            HiddenAt = meta.HiddenAt,
                        });
                    }
                    catch { }
                }
            }
            return results;
        }

        #endregion
    }
}
