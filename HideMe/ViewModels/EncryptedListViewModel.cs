using System;
using System.Collections.ObjectModel;
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
    public class EncryptedListViewModel : Screen
    {
        public ObservableCollection<HiddenItem> Items { get; } = new ObservableCollection<HiddenItem>();
        public HiddenItem SelectedItem { get; set; }
        public bool IsScanning { get; set; }

        [DependsOn(nameof(IsScanning))]
        public bool CanScan => !IsScanning;
        [DependsOn(nameof(SelectedItem))]
        public bool CanShow => SelectedItem != null;

        public EncryptedListViewModel()
        {
            DisplayName = "隐藏列表";
        }

        public async Task Scan()
        {
            if (IsScanning) return;
            IsScanning = true;
            try
            {
                var found = await Task.Run(() => AdStreamService.ScanAll());
                Items.Clear();
                foreach (var it in found) Items.Add(it);
                if (found.Count == 0)
                {
                    MessageBox.Show("未发现隐藏项。", "HideMe", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("扫描失败：" + ex.Message, "HideMe",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsScanning = false;
            }
        }

        public async Task Show()
        {
            var item = SelectedItem;
            if (item == null) return;
            try
            {
                await Task.Run(() =>
                {
                    if (item.Kind == "Folder")
                        AdStreamService.ShowFolder(item.OriginalPath, item.VaultPath, item.StreamId);
                    else
                        AdStreamService.ShowFile(item.OriginalPath, item.VaultPath, item.StreamId);
                });
                Items.Remove(item);
            }
            catch (Exception ex)
            {
                MessageBox.Show("恢复失败：" + ex.Message, "HideMe",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
