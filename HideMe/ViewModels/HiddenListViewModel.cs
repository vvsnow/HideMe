using System;
using System.Collections.ObjectModel;
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
    /// 隐藏列表页：动态扫描各盘回收站 SID 目录，可恢复（显示）选中项。
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class HiddenListViewModel : Screen, IHandle<VaultChangedMessage>
    {
        private readonly IEventAggregator _events;
        private CancellationTokenSource _cts;
        private bool _hasScanned;
        private bool _needsRefresh;

        public ObservableCollection<HiddenItem> Items { get; } = new ObservableCollection<HiddenItem>();
        public HiddenItem SelectedItem { get; set; }

        public bool IsBusy { get; set; }
        public string StatusText { get; set; }
        public double ProgressValue { get; set; }
        public bool IsIndeterminate { get; set; }

        [DependsOn(nameof(IsBusy))]
        public bool CanScan => !IsBusy;

        [DependsOn(nameof(SelectedItem), nameof(IsBusy))]
        public bool CanShow => SelectedItem != null && !IsBusy && SelectedItem.Status != "数据丢失";

        [DependsOn(nameof(IsBusy))]
        public bool CanCancel => IsBusy;

        public HiddenListViewModel(IEventAggregator events)
        {
            _events = events;
            _events.SubscribeOnUIThread(this);
            DisplayName = "隐藏列表";
        }

        public async Task HandleAsync(VaultChangedMessage message, CancellationToken cancellationToken)
        {
            _needsRefresh = true;
            if (IsActive && !IsBusy)
                await Scan();
        }

        protected override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            await base.OnActivateAsync(cancellationToken);
            if (!_hasScanned || _needsRefresh)
                await Scan();
        }

        public void Cancel()
        {
            try { _cts?.Cancel(); } catch { }
        }

        public async Task Scan()
        {
            if (IsBusy) return;

            IsBusy = true;
            StatusText = "扫描中…";
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
                var result = await Task.Run(() => AdStreamService.ScanAll(progress, token), token);

                Items.Clear();
                foreach (var item in result.Items)
                    Items.Add(item);

                _hasScanned = true;
                _needsRefresh = false;

                if (result.Skipped > 0)
                    StatusText = "已跳过 " + result.Skipped + " 个无法读取的条目（详见日志）。";
                else
                    StatusText = result.Items.Count == 0 ? "未发现隐藏项。" : "";
            }
            catch (OperationCanceledException)
            {
                StatusText = "已取消。";
            }
            catch (Exception ex)
            {
                LogService.Error("扫描失败", ex);
                StatusText = "";
                MessageBox.Show("扫描失败：" + ex.Message + "\r\n\r\n详情见日志：" + LogService.Directory,
                    "HideMe", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 0;
                IsIndeterminate = false;
                try { _cts.Dispose(); } catch { }
                _cts = null;
            }
        }

        public async Task Show()
        {
            if (IsBusy) return;
            var item = SelectedItem;
            if (item == null || item.Status == "数据丢失") return;

            IsBusy = true;
            StatusText = "准备恢复…";
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

            Func<bool, ShowResult> restore = overwrite =>
                item.Kind == "Folder"
                    ? AdStreamService.ShowFolder(item.OriginalPath, item.VaultPath, item.StreamId,
                        overwrite, progress, token)
                    : AdStreamService.ShowFile(item.OriginalPath, item.VaultPath, item.StreamId,
                        overwrite, progress, token);

            try
            {
                ShowResult result;
                try
                {
                    result = await Task.Run(() => restore(false), token);
                }
                catch (TargetExistsException)
                {
                    if (MessageBox.Show("目标已存在：\r\n" + item.OriginalPath +
                                        "\r\n\r\n是否覆盖？", "HideMe",
                            MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
                        return;

                    result = await Task.Run(() => restore(true), token);
                }

                if (result.VaultDataDeleted)
                {
                    Items.Remove(item);
                    StatusText = "恢复成功。";
                }
                else
                {
                    item.Status = "已恢复，副本未清理";
                    StatusText = "已恢复，但保险库副本未能删除。";
                }

                if (!string.IsNullOrEmpty(result.CleanupError))
                {
                    MessageBox.Show("已恢复，但" + result.CleanupError +
                                    "\r\n\r\n可稍后重试恢复以清理保险库副本。", "HideMe",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("已取消，原条目未被修改。", "HideMe",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogService.Error("恢复失败：" + item.OriginalPath, ex);
                MessageBox.Show("恢复失败：" + ex.Message + "\r\n\r\n详情见日志：" + LogService.Directory,
                    "HideMe", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                ProgressValue = 0;
                IsIndeterminate = false;
                try { _cts.Dispose(); } catch { }
                _cts = null;
            }
        }
    }
}
