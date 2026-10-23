using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HideMe.Services
{
    /// <summary>
    /// 通过 SHGetFileInfo 获取 16x16 的 Shell 图标，并按盘符/文件夹/扩展名缓存。
    /// </summary>
    public static class ShellIconService
    {
        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_SMALLICON = 0x000000001;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;

        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

        private static readonly object SyncRoot = new object();
        private static readonly Dictionary<string, ImageSource> Cache =
            new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
            ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public static ImageSource GetIcon(string path, bool isDirectory, bool isDrive)
        {
            var key = BuildKey(path, isDirectory, isDrive);
            lock (SyncRoot)
            {
                if (Cache.ContainsKey(key))
                    return Cache[key];
            }

            ImageSource image = null;
            try
            {
                var info = new SHFILEINFO();
                var flags = SHGFI_ICON | SHGFI_SMALLICON;
                var attributes = 0u;
                var query = path;

                if (isDrive)
                {
                    // 盘符直接查询根路径，不使用 USEFILEATTRIBUTES，图标更准确
                }
                else if (isDirectory)
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    attributes = FILE_ATTRIBUTE_DIRECTORY;
                    query = "folder";
                }
                else
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                    attributes = FILE_ATTRIBUTE_NORMAL;
                    query = "file" + (Path.GetExtension(path) ?? string.Empty);
                }

                var result = SHGetFileInfo(query, attributes, ref info,
                    (uint)Marshal.SizeOf(typeof(SHFILEINFO)), flags);
                if (result != IntPtr.Zero && info.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        image = Imaging.CreateBitmapSourceFromHIcon(
                            info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        if (image != null) image.Freeze();
                    }
                    finally
                    {
                        DestroyIcon(info.hIcon);
                    }
                }
            }
            catch
            {
                image = null;
            }

            lock (SyncRoot)
            {
                Cache[key] = image;
            }
            return image;
        }

        private static string BuildKey(string path, bool isDirectory, bool isDrive)
        {
            if (isDrive)
                return "drive:" + (path ?? string.Empty).TrimEnd('\\', '/').ToLowerInvariant();
            if (isDirectory)
                return "folder";
            return "ext:" + (Path.GetExtension(path) ?? string.Empty).ToLowerInvariant();
        }
    }
}
