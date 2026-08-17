using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using HideMe.Models;
using HideMe.Services;
using PropertyChanged;

namespace HideMe.ViewModels
{
    /// <summary>
    /// 文件浏览器页：盘符树 + 对选中文件/文件夹执行隐藏。
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class BrowserViewModel : Screen
    {
        private readonly SettingsService _settings;

        public ObservableCollection<FileSystemNode> RootNodes { get; } = new ObservableCollection<FileSystemNode>();
        public FileSystemNode SelectedNode { get; set; }

        [DependsOn(nameof(SelectedNode))]
        public bool CanHide => SelectedNode != null;

        public BrowserViewModel(SettingsService settings)
        {
            _settings = settings;
            DisplayName = "文件浏览器";
            LoadRoots();
        }

        private void LoadRoots()
        {
            RootNodes.Clear();
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    RootNodes.Add(FileSystemNode.ForDirectory(d.RootDirectory.FullName, _settings.ShowHiddenFiles));
            }
        }

        public void Reload() => LoadRoots();

        public async Task Hide()
        {
            var node = SelectedNode;
            if (node == null) return;

            if (_settings.ConfirmBeforeHide &&
                MessageBox.Show("确定要隐藏 \"" + node.FullPath + "\" 吗？\r\n\r\n" +
                                "隐藏后该条目会从磁盘删除，内容存放在其所在盘的回收站隐藏目录中，\r\n" +
                                "可随时在\"隐藏列表\"页扫描并恢复。",
                    "HideMe", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            try
            {
                await Task.Run(() =>
                {
                    if (node.IsDirectory) AdStreamService.HideFolder(node.FullPath);
                    else AdStreamService.HideFile(node.FullPath);
                });
                LoadRoots();   // 隐藏成功后刷新树
                MessageBox.Show("隐藏成功。可在\"隐藏列表\"页查看并恢复。", "HideMe",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("隐藏失败：" + ex.Message, "HideMe",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
