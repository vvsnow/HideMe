using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Windows.Media;
using Caliburn.Micro;
using HideMe.Services;

namespace HideMe.Models
{
    /// <summary>
    /// 文件树节点：懒加载，展开时才填充子项。
    /// </summary>
    public class FileSystemNode : PropertyChangedBase
    {
        private bool _isExpanded;
        private bool _isSelected;
        private bool _loaded;
        private ImageSource _icon;

        public FileSystemNode(string name, string fullPath, bool isDirectory)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
        }

        public string Name { get; }
        public string FullPath { get; }
        public bool IsDirectory { get; }
        public bool ShowHiddenFiles { get; set; }

        /// <summary>是否为盘符根节点。</summary>
        public bool IsDrive
        {
            get
            {
                if (!IsDirectory || string.IsNullOrEmpty(FullPath)) return false;
                var root = Path.GetPathRoot(FullPath);
                if (string.IsNullOrEmpty(root)) return false;
                return string.Equals(root.TrimEnd('\\', '/'), FullPath.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Shell 图标（懒加载并缓存）。</summary>
        public ImageSource Icon
        {
            get
            {
                if (_icon == null)
                    _icon = ShellIconService.GetIcon(FullPath, IsDirectory, IsDrive);
                return _icon;
            }
        }

        public ObservableCollection<FileSystemNode> Children { get; } = new ObservableCollection<FileSystemNode>();

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (Set(ref _isExpanded, value) && value && IsDirectory)
                    EnsureChildren();
            }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { Set(ref _isSelected, value); }
        }

        /// <summary>首次展开时加载子项（清掉占位节点）。</summary>
        public void EnsureChildren()
        {
            if (_loaded) return;
            _loaded = true;
            Children.Clear();
            try
            {
                foreach (var d in Directory.GetDirectories(FullPath))
                {
                    try
                    {
                        if (!ShowHiddenFiles && IsHiddenOrSystem(d)) continue;
                        Children.Add(ForDirectory(d, ShowHiddenFiles));
                    }
                    catch { }
                }
                foreach (var f in Directory.GetFiles(FullPath))
                {
                    try
                    {
                        if (!ShowHiddenFiles && IsHiddenOrSystem(f)) continue;
                        Children.Add(new FileSystemNode(Path.GetFileName(f), f, false));
                    }
                    catch { }
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException
                                       || ex is IOException
                                       || ex is SecurityException)
            {
                LogService.Warn("枚举目录失败：" + FullPath, ex);
                Children.Add(new FileSystemNode("（无法访问）", FullPath, false));
            }
        }

        private static bool IsHiddenOrSystem(string path)
        {
            var attrs = File.GetAttributes(path);
            return (attrs & (FileAttributes.Hidden | FileAttributes.System)) != 0;
        }

        public static FileSystemNode ForDirectory(string fullPath, bool showHiddenFiles)
        {
            var trimmed = fullPath.TrimEnd('\\', '/');
            var name = Path.GetFileName(trimmed);
            if (string.IsNullOrEmpty(name)) name = fullPath;   // 盘符根目录如 "C:\"
            var node = new FileSystemNode(name, fullPath, true) { ShowHiddenFiles = showHiddenFiles };
            node.Children.Add(new FileSystemNode("加载中…", fullPath, false));   // 占位，保证有展开箭头
            return node;
        }
    }
}
