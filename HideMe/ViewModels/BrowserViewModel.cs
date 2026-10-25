using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
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
    public class BrowserViewModel : Screen
    {
        private readonly SettingsService _settings;
        private readonly IEventAggregator _events;
        private bool _shownHiddenFiles;
        private CancellationTokenSource _cts;

        public ObservableCollection<FileSystemNode> RootNodes { get; } = new ObservableCollection<FileSystemNode>();
        public FileSystemNode SelectedNode { get; set; }

        public bool IsBusy { get; set; }
        public string StatusText { get; set; }
        public double ProgressValue { get; set; }
        public bool IsIndeterminate { get; set; }

        [DependsOn(nameof(SelectedNode), nameof(IsBusy))]
        public bool CanHide => SelectedNode != null && !IsBusy;

        [DependsOn(nameof(IsBusy))]
        public bool CanReload => !IsBusy;

        [DependsOn(nameof(SelectedNode), nameof(IsBusy))]
        public bool CanOpenInExplorer => SelectedNode != null && !IsBusy;

        [DependsOn(nameof(IsBusy))]
        public bool CanCancel => IsBusy;

        public BrowserViewModel(SettingsService settings, IEventAggregator events)
        {
            _settings = settings;
            _events = events;
            _shownHiddenFiles = settings.ShowHiddenFiles;
            settings.Changed += HandleSettingsChanged;
            DisplayName = "文件浏览器";
            LoadRoots();
        }

        /// <summary>
        /// TreeView 选中项变化。由 XAML 的 Caliburn 事件绑定转发：
        /// [Event SelectedItemChanged] = [HandleSelectedItemChanged($eventArgs)]
        /// </summary>
        public void HandleSelectedItemChanged(RoutedPropertyChangedEventArgs<object> e)
        {
            SelectedNode = e.NewValue as FileSystemNode;
        }

        private void HandleSettingsChanged(object sender, EventArgs e)
        {
            if (_shownHiddenFiles == _settings.ShowHiddenFiles) return;
            _shownHiddenFiles = _settings.ShowHiddenFiles;
            if (!IsBusy) ReloadPreservingState();
        }

        private void LoadRoots()
        {
            RootNodes.Clear();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                        RootNodes.Add(FileSystemNode.ForDirectory(d.RootDirectory.FullName, _settings.ShowHiddenFiles));
                }
                catch (Exception ex)
                {
                    LogService.Warn("读取磁盘信息失败", ex);
                }
            }
        }

        public void Reload()
        {
            if (IsBusy) return;
            ReloadPreservingState();
        }

        /// <summary>重建文件树，同时恢复已展开节点和当前选中项。</summary>
        private void ReloadPreservingState()
        {
            var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectExpanded(RootNodes, expanded);

            var selectedPath = SelectedNode != null ? SelectedNode.FullPath : null;

            LoadRoots();
            RestoreExpanded(RootNodes, expanded);

            if (!string.IsNullOrEmpty(selectedPath))
            {
                var node = FindNode(RootNodes, selectedPath);
                if (node != null)
                {
                    node.IsSelected = true;
                    SelectedNode = node;
                    return;
                }
            }

            SelectedNode = null;
        }

        private static void CollectExpanded(IEnumerable<FileSystemNode> nodes, HashSet<string> expanded)
        {
            foreach (var node in nodes)
            {
                if (!node.IsDirectory || !node.IsExpanded) continue;
                expanded.Add(node.FullPath);
                CollectExpanded(node.Children, expanded);
            }
        }

        private static void RestoreExpanded(IEnumerable<FileSystemNode> nodes, HashSet<string> expanded)
        {
            foreach (var node in nodes)
            {
                if (!node.IsDirectory) continue;
                if (!expanded.Contains(node.FullPath)) continue;

                node.IsExpanded = true;   // 同步加载子节点
                RestoreExpanded(node.Children, expanded);
            }
        }

        private static FileSystemNode FindNode(IEnumerable<FileSystemNode> nodes, string path)
        {
            foreach (var node in nodes)
            {
                if (string.Equals(node.FullPath, path, StringComparison.OrdinalIgnoreCase))
                    return node;

                if (node.IsExpanded)
                {
                    var found = FindNode(node.Children, path);
                    if (found != null) return found;
                }
            }
            return null;
        }

        public void OpenInExplorer()
        {
            var node = SelectedNode;
            if (node == null) return;

            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + node.FullPath + "\"")
                {
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                LogService.Error("打开资源管理器失败：" + node.FullPath, ex);
                MessageBox.Show("打开资源管理器失败：" + ex.Message, "HideMe",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void Cancel()
        {
            try { _cts?.Cancel(); } catch { }
        }

        public async Task Hide()
        {
            if (IsBusy) return;
            var node = SelectedNode;
            if (node == null) return;

            if (_settings.ConfirmBeforeHide &&
                MessageBox.Show("确定要隐藏 \"" + node.FullPath + "\" 吗？\r\n\r\n" +
                                "隐藏后该条目会从磁盘删除，内容存放在其所在盘的回收站隐藏目录中，\r\n" +
                                "可随时在\"隐藏列表\"页扫描并恢复。",
                    "HideMe", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK)
                return;

            IsBusy = true;
            StatusText = "准备中…";
            ProgressValue = 0;
            IsIndeterminate = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            var progress = new Progress<ProgressInfo>(p =>
            {
                if (!IsBusy) return;
                StatusText = p.Stage;
                if (p.Total > 0)
                {
                    IsIndeterminate = false;
                    ProgressValue = Math.Max(0, Math.Min(100, p.Processed * 100.0 / p.Total));
                }
                else
                {
                    IsIndeterminate = true;
                    ProgressValue = 0;
                }
            });

            try
            {
                var result = await Task.Run(() =>
                    node.IsDirectory
                        ? AdStreamService.HideFolder(node.FullPath, progress, token)
                        : AdStreamService.HideFile(node.FullPath, progress, token), token);

                ReloadPreservingState();
                try
                {
                    await _events.PublishOnUIThreadAsync(new VaultChangedMessage());
                }
                catch (Exception ex)
                {
                    LogService.Warn("发布保险库变更通知失败", ex);
                }

                var message = "隐藏成功。可在\"隐藏列表\"页查看并恢复。";
                if (result.UsedFallback)
                    message += "\r\n\r\n注意：无法在目标盘创建保险库，内容实际存放在 " +
                               Path.GetPathRoot(result.VaultPath) + "。";
                MessageBox.Show(message, "HideMe", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("已取消，原条目未被修改。", "HideMe",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogService.Error("隐藏失败：" + node.FullPath, ex);
                MessageBox.Show("隐藏失败：" + ex.Message + "\r\n\r\n详情见日志：" + LogService.Directory,
                    "HideMe", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                StatusText = "";
                ProgressValue = 0;
                IsIndeterminate = false;
                try { _cts.Dispose(); } catch { }
                _cts = null;
            }
        }
    }
}
