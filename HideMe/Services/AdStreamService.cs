using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
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

        private const int ERROR_FILE_NOT_FOUND = 2;
        private const int ERROR_PATH_NOT_FOUND = 3;

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
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
                return @"\\?\UNC\" + path.Substring(2);
            return @"\\?\" + path;
        }

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

        private static string CopyFileToStream(string fromPath, string toPath, string toStream,
            string stage, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            using (var src = new FileStream(fromPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            using (var dst = OpenStream(toPath, toStream, FileAccess.Write, FileMode.Create))
                return CopyWithHash(src, dst, src.Length, stage, progress, ct);
        }

        private static string CopyStreamToFile(string fromPath, string fromStream, string toPath,
            string stage, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            using (var src = OpenStream(fromPath, fromStream, FileAccess.Read, FileMode.Open))
            using (var dst = new FileStream(toPath, FileMode.Create, FileAccess.Write, FileShare.None))
                return CopyWithHash(src, dst, src.Length, stage, progress, ct);
        }

        /// <summary>边复制边计算 SHA-256，并支持进度与取消。</summary>
        private static string CopyWithHash(Stream src, Stream dst, long total,
            string stage, IProgress<ProgressInfo> progress, CancellationToken ct)
        {
            using (var sha = SHA256.Create())
            {
                var buffer = new byte[81920];
                long done = 0;
                long lastReport = 0;
                int read;
                while ((read = src.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    dst.Write(buffer, 0, read);
                    sha.TransformBlock(buffer, 0, read, null, 0);
                    done += read;
                    if (progress != null && (done - lastReport >= 4 * 1024 * 1024 || done == total))
                    {
                        lastReport = done;
                        progress.Report(new ProgressInfo { Stage = stage, Processed = done, Total = total });
                    }
                }
                sha.TransformFinalBlock(new byte[0], 0, 0);

                if (progress != null && total == 0)
                    progress.Report(new ProgressInfo { Stage = stage, Processed = done, Total = done });

                return Convert.ToBase64String(sha.Hash);
            }
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

        /// <summary>删除数据流；流本来不存在时视为成功（幂等），其它失败抛 Win32Exception。</summary>
        public static void DeleteStream(string path, string stream)
        {
            if (DeleteFileW(AdsPath(path, stream))) return;
            var error = Marshal.GetLastWin32Error();
            if (error == ERROR_FILE_NOT_FOUND || error == ERROR_PATH_NOT_FOUND) return;
            throw new Win32Exception(error, "删除数据流失败：" + stream);
        }

        /// <summary>尽力删除数据流，失败只记录日志。</summary>
        public static bool TryDeleteStream(string path, string stream)
        {
            try
            {
                DeleteStream(path, stream);
                return true;
            }
            catch (Exception ex)
            {
                LogService.Warn("清理数据流失败：" + path + ":" + stream, ex);
                return false;
            }
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
            bool unused;
            return GetVaultDirectory(path, out unused);
        }

        public static string GetVaultDirectory(string path, out bool usedFallback)
        {
            usedFallback = false;
            var full = Path.GetFullPath(path);
            var driveRoot = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(driveRoot))
                throw new NotSupportedException("无法确定目标文件所在的磁盘。");

            var primary = Path.Combine(driveRoot, "$Recycle.Bin", CurrentSid);
            if (Directory.Exists(primary)) return primary;

            try
            {
                Directory.CreateDirectory(primary);
                return primary;
            }
            catch (Exception ex)
            {
                LogService.Warn("无法在目标盘创建保险库，回退到系统盘：" + primary, ex);
                var systemDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
                var fallback = Path.Combine(systemDrive, "$Recycle.Bin", CurrentSid);
                try
                {
                    Directory.CreateDirectory(fallback);
                }
                catch (Exception fallbackEx)
                {
                    LogService.Error("创建系统盘保险库失败：" + fallback, fallbackEx);
                }
                usedFallback = true;
                return fallback;
            }
        }

        /// <summary>确认目标卷为 NTFS（ADS 的前提）。</summary>
        private static void EnsureAdsCapable(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root))
                throw new NotSupportedException("无法确定目标文件所在的磁盘。");
            if (root.StartsWith(@"\\", StringComparison.Ordinal))
                throw new NotSupportedException("暂不支持网络路径。");

            DriveInfo drive;
            try
            {
                drive = new DriveInfo(root);
            }
            catch (Exception ex)
            {
                throw new NotSupportedException("无法读取目标磁盘信息：" + root, ex);
            }

            bool ready;
            string format;
            try
            {
                ready = drive.IsReady;
                format = ready ? drive.DriveFormat : "";
            }
            catch (Exception ex)
            {
                throw new NotSupportedException("无法读取目标磁盘信息：" + root, ex);
            }

            if (!ready)
                throw new IOException("目标磁盘未就绪：" + root);
            if (!string.Equals(format, "NTFS", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("目标磁盘 " + root + " 的文件系统是 " + format +
                    "，不支持 NTFS 替代数据流(ADS)。请选择 NTFS 磁盘上的文件或文件夹。");
        }

        #endregion

        #region 隐藏 / 显示

        public static HideResult HideFile(string path,
            IProgress<ProgressInfo> progress = null, CancellationToken ct = default)
        {
            var full = Path.GetFullPath(path);
            if (!File.Exists(full)) throw new FileNotFoundException("文件不存在", full);
            EnsureAdsCapable(full);

            var vault = GetVaultDirectory(full, out bool usedFallback);
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            var dataStream = DataPrefix + id;
            var metaStream = MetaPrefix + id;
            var hiddenAt = DateTime.Now;

            progress?.Report(new ProgressInfo { Stage = "写入保险库", Total = 0 });
            try
            {
                var hash = CopyFileToStream(full, vault, dataStream, "写入保险库", progress, ct);
                ct.ThrowIfCancellationRequested();
                WriteText(vault, metaStream, HiddenMeta.ToJson(new HiddenMeta
                {
                    Kind = "File",
                    OriginalPath = full,
                    HiddenAt = hiddenAt,
                    Sha256 = hash,
                }));

                try { File.SetAttributes(full, File.GetAttributes(full) & ~FileAttributes.ReadOnly); } catch { }
                ct.ThrowIfCancellationRequested();
                progress?.Report(new ProgressInfo { Stage = "删除原文件", Total = 0 });
                File.Delete(full);

                HideIndex.Upsert(new IndexEntry
                {
                    Id = id,
                    Kind = "File",
                    OriginalPath = full,
                    VaultPath = vault,
                    HiddenAt = hiddenAt,
                    Sha256 = hash,
                });

                return new HideResult { VaultPath = vault, UsedFallback = usedFallback };
            }
            catch
            {
                // 回滚：删除刚写入的流，原文件保持不动
                TryDeleteStream(vault, dataStream);
                TryDeleteStream(vault, metaStream);
                throw;
            }
        }

        public static HideResult HideFolder(string path,
            IProgress<ProgressInfo> progress = null, CancellationToken ct = default)
        {
            var full = Path.GetFullPath(path).TrimEnd('\\', '/');
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("文件夹不存在");
            EnsureAdsCapable(full);

            var vault = GetVaultDirectory(full, out bool usedFallback);
            var id = Guid.NewGuid().ToString("N").Substring(0, 8);
            var dataStream = DataPrefix + id;
            var metaStream = MetaPrefix + id;
            var hiddenAt = DateTime.Now;
            var tmpZip = Path.Combine(Path.GetTempPath(), "hideme_" + id + ".zip");

            try
            {
                progress?.Report(new ProgressInfo { Stage = "压缩文件夹", Total = 0 });
                ct.ThrowIfCancellationRequested();
                ZipFile.CreateFromDirectory(full, tmpZip, CompressionLevel.Optimal, false);
                ct.ThrowIfCancellationRequested();

                var hash = CopyFileToStream(tmpZip, vault, dataStream, "写入保险库", progress, ct);
                ct.ThrowIfCancellationRequested();
                WriteText(vault, metaStream, HiddenMeta.ToJson(new HiddenMeta
                {
                    Kind = "Folder",
                    OriginalPath = full,
                    HiddenAt = hiddenAt,
                    Sha256 = hash,
                }));

                ct.ThrowIfCancellationRequested();
                progress?.Report(new ProgressInfo { Stage = "删除原文件夹", Total = 0 });
                Directory.Delete(full, true);

                HideIndex.Upsert(new IndexEntry
                {
                    Id = id,
                    Kind = "Folder",
                    OriginalPath = full,
                    VaultPath = vault,
                    HiddenAt = hiddenAt,
                    Sha256 = hash,
                });

                return new HideResult { VaultPath = vault, UsedFallback = usedFallback };
            }
            catch
            {
                TryDeleteStream(vault, dataStream);
                TryDeleteStream(vault, metaStream);
                throw;
            }
            finally
            {
                try { File.Delete(tmpZip); } catch { }
            }
        }

        public static ShowResult ShowFile(string originalPath, string vaultPath, string streamId,
            bool overwrite = false, IProgress<ProgressInfo> progress = null, CancellationToken ct = default)
        {
            var dataStream = DataPrefix + streamId;
            var metaStream = MetaPrefix + streamId;
            var full = Path.GetFullPath(originalPath);
            var dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir))
                throw new NotSupportedException("无效的目标路径。");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!overwrite && PathExists(full))
                throw new TargetExistsException(full);

            var temp = Path.Combine(dir, "~hideme_restore_" + Guid.NewGuid().ToString("N") + ".tmp");
            string backup = null;

            try
            {
                progress?.Report(new ProgressInfo { Stage = "恢复文件", Total = 0 });
                var expected = ReadExpectedHash(vaultPath, metaStream, streamId);
                var actual = CopyStreamToFile(vaultPath, dataStream, temp, "恢复文件", progress, ct);
                if (!string.IsNullOrEmpty(expected)
                    && !string.Equals(expected, actual, StringComparison.Ordinal))
                    throw new InvalidDataException("隐藏数据校验失败，保险库副本已保留。");

                ct.ThrowIfCancellationRequested();

                if (PathExists(full))
                {
                    backup = MakeBackupPath(full);
                    MovePath(full, backup);
                    try
                    {
                        File.Move(temp, full);
                    }
                    catch
                    {
                        try { MovePath(backup, full); }
                        catch (Exception ex) { LogService.Error("恢复回滚失败：" + full, ex); }
                        throw;
                    }
                    try { DeletePath(backup); }
                    catch (Exception ex) { LogService.Warn("删除恢复备份失败：" + backup, ex); }
                }
                else
                {
                    File.Move(temp, full);
                }

                return DeleteVaultStreams(vaultPath, dataStream, metaStream, streamId, full);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); } catch { }
            }
        }

        public static ShowResult ShowFolder(string originalPath, string vaultPath, string streamId,
            bool overwrite = false, IProgress<ProgressInfo> progress = null, CancellationToken ct = default)
        {
            var dataStream = DataPrefix + streamId;
            var metaStream = MetaPrefix + streamId;
            var full = Path.GetFullPath(originalPath).TrimEnd('\\', '/');
            var dir = Path.GetDirectoryName(full);
            if (string.IsNullOrEmpty(dir))
                throw new NotSupportedException("不支持恢复到磁盘根目录。");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            if (!overwrite && PathExists(full))
                throw new TargetExistsException(full);

            var tmpZip = Path.Combine(Path.GetTempPath(), "hideme_" + streamId + "_restore.zip");
            var staging = Path.Combine(dir, "~hideme_restore_" + Guid.NewGuid().ToString("N"));
            string backup = null;

            try
            {
                progress?.Report(new ProgressInfo { Stage = "读取保险库", Total = 0 });
                var expected = ReadExpectedHash(vaultPath, metaStream, streamId);
                var actual = CopyStreamToFile(vaultPath, dataStream, tmpZip, "读取保险库", progress, ct);
                if (!string.IsNullOrEmpty(expected)
                    && !string.Equals(expected, actual, StringComparison.Ordinal))
                    throw new InvalidDataException("隐藏数据校验失败，保险库副本已保留。");

                ct.ThrowIfCancellationRequested();
                progress?.Report(new ProgressInfo { Stage = "解压", Total = 0 });
                Directory.CreateDirectory(staging);
                ZipFile.ExtractToDirectory(tmpZip, staging);
                ct.ThrowIfCancellationRequested();

                if (PathExists(full))
                {
                    backup = MakeBackupPath(full);
                    MovePath(full, backup);
                    try
                    {
                        Directory.Move(staging, full);
                    }
                    catch
                    {
                        try { MovePath(backup, full); }
                        catch (Exception ex) { LogService.Error("恢复回滚失败：" + full, ex); }
                        throw;
                    }
                    try { DeletePath(backup); }
                    catch (Exception ex) { LogService.Warn("删除恢复备份失败：" + backup, ex); }
                }
                else
                {
                    Directory.Move(staging, full);
                }

                return DeleteVaultStreams(vaultPath, dataStream, metaStream, streamId, full);
            }
            finally
            {
                try { File.Delete(tmpZip); } catch { }
                try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
            }
        }

        /// <summary>扫描固定/可移动盘的保险库，合并元数据流与本地索引。</summary>
        public static ScanResult ScanAll(IProgress<ProgressInfo> progress = null, CancellationToken ct = default)
        {
            var result = new ScanResult();
            var sid = CurrentSid;
            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable)
                .ToArray();

            var vaults = new List<string>();
            var indexEntries = HideIndex.GetAll();
            var indexById = new Dictionary<string, IndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in indexEntries)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.Id))
                    indexById[entry.Id] = entry;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scannedEntries = new List<IndexEntry>();

            for (int i = 0; i < drives.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var drive = drives[i];
                progress?.Report(new ProgressInfo
                {
                    Stage = "扫描 " + drive.Name,
                    Processed = i,
                    Total = drives.Length,
                });

                try
                {
                    if (!drive.IsReady) continue;
                    var vault = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin", sid);
                    if (!Directory.Exists(vault)) continue;
                    vaults.Add(vault);

                    var streams = EnumerateStreams(vault);
                    var streamSet = new HashSet<string>(streams, StringComparer.OrdinalIgnoreCase);

                    foreach (var stream in streams)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!stream.StartsWith(MetaPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                        var id = stream.Substring(MetaPrefix.Length);

                        try
                        {
                            var meta = HiddenMeta.FromJson(ReadText(vault, stream));
                            if (meta == null || string.IsNullOrEmpty(meta.OriginalPath))
                            {
                                result.Skipped++;
                                LogService.Warn("隐藏项元数据无效：" + vault + ":" + stream);
                                continue;
                            }

                            seen.Add(id);
                            var hasData = streamSet.Contains(DataPrefix + id);
                            var item = new HiddenItem
                            {
                                OriginalPath = meta.OriginalPath,
                                VaultPath = vault,
                                StreamId = id,
                                Kind = meta.Kind,
                                HiddenAt = meta.HiddenAt,
                                Status = hasData ? "正常" : "数据丢失",
                            };
                            if (!hasData)
                                LogService.Warn("隐藏项数据流缺失：" + id);

                            result.Items.Add(item);
                            scannedEntries.Add(new IndexEntry
                            {
                                Id = id,
                                Kind = meta.Kind,
                                OriginalPath = meta.OriginalPath,
                                VaultPath = vault,
                                HiddenAt = meta.HiddenAt,
                                Sha256 = meta.Sha256,
                            });
                        }
                        catch (Exception ex)
                        {
                            result.Skipped++;
                            LogService.Warn("读取隐藏项失败：" + vault + ":" + stream, ex);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Skipped++;
                    LogService.Warn("扫描磁盘失败：" + drive.Name, ex);
                }
            }

            // 元数据流缺失时，用本地索引恢复条目；数据流也不在则标记丢失。
            foreach (var pair in indexById)
            {
                ct.ThrowIfCancellationRequested();
                if (seen.Contains(pair.Key)) continue;

                var entry = pair.Value;
                string dataVault = null;

                if (!string.IsNullOrEmpty(entry.VaultPath)
                    && Directory.Exists(entry.VaultPath)
                    && StreamExists(entry.VaultPath, DataPrefix + entry.Id))
                {
                    dataVault = entry.VaultPath;
                }
                else
                {
                    foreach (var vault in vaults)
                    {
                        if (StreamExists(vault, DataPrefix + entry.Id))
                        {
                            dataVault = vault;
                            break;
                        }
                    }
                }

                if (dataVault != null)
                {
                    result.Items.Add(new HiddenItem
                    {
                        OriginalPath = entry.OriginalPath,
                        VaultPath = dataVault,
                        StreamId = entry.Id,
                        Kind = entry.Kind,
                        HiddenAt = entry.HiddenAt,
                        Status = "元数据缺失（索引可恢复）",
                    });
                    scannedEntries.Add(new IndexEntry
                    {
                        Id = entry.Id,
                        Kind = entry.Kind,
                        OriginalPath = entry.OriginalPath,
                        VaultPath = dataVault,
                        HiddenAt = entry.HiddenAt,
                        Sha256 = entry.Sha256,
                    });
                }
                else
                {
                    result.Items.Add(new HiddenItem
                    {
                        OriginalPath = entry.OriginalPath,
                        VaultPath = entry.VaultPath,
                        StreamId = entry.Id,
                        Kind = entry.Kind,
                        HiddenAt = entry.HiddenAt,
                        Status = "数据丢失",
                    });
                }
            }

            if (scannedEntries.Count > 0)
                HideIndex.UpsertRange(scannedEntries);

            progress?.Report(new ProgressInfo
            {
                Stage = "扫描完成",
                Processed = drives.Length,
                Total = drives.Length,
            });

            return result;
        }

        #endregion

        #region 内部辅助

        private static string ReadExpectedHash(string vaultPath, string metaStream, string streamId)
        {
            try
            {
                if (StreamExists(vaultPath, metaStream))
                {
                    var meta = HiddenMeta.FromJson(ReadText(vaultPath, metaStream));
                    if (meta != null && !string.IsNullOrEmpty(meta.Sha256))
                        return meta.Sha256;
                }
            }
            catch (Exception ex)
            {
                LogService.Warn("读取隐藏元数据失败：" + streamId, ex);
            }

            var entry = HideIndex.Find(streamId);
            return entry != null ? entry.Sha256 : null;
        }

        private static ShowResult DeleteVaultStreams(string vaultPath, string dataStream, string metaStream,
            string streamId, string restoredPath)
        {
            var result = new ShowResult { RestoredPath = restoredPath };

            try
            {
                DeleteStream(vaultPath, dataStream);
                result.VaultDataDeleted = true;
                HideIndex.Remove(streamId);
            }
            catch (Exception ex)
            {
                result.CleanupError = "保险库数据副本删除失败：" + ex.Message;
                LogService.Warn("保险库数据副本删除失败：" + vaultPath + ":" + dataStream, ex);
                return result;   // 保留元数据与索引，便于重试
            }

            try
            {
                DeleteStream(vaultPath, metaStream);
                result.VaultMetaDeleted = true;
            }
            catch (Exception ex)
            {
                result.CleanupError = "元数据副本删除失败：" + ex.Message;
                LogService.Warn("保险库元数据删除失败：" + vaultPath + ":" + metaStream, ex);
            }

            return result;
        }

        private static bool PathExists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        private static void MovePath(string source, string destination)
        {
            if (Directory.Exists(source)) Directory.Move(source, destination);
            else File.Move(source, destination);
        }

        private static void DeletePath(string path)
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }

        private static string MakeBackupPath(string full)
        {
            return full + ".hideme_backup_" + DateTime.Now.ToString("yyyyMMddHHmmssfff") + "_" +
                   Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        #endregion
    }
}
