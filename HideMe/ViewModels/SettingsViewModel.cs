using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Caliburn.Micro;
using HideMe.Services;
using PropertyChanged;

namespace HideMe.ViewModels
{
    /// <summary>
    /// 设置页：隐藏前确认、是否显示隐藏/系统文件。
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class SettingsViewModel : Screen
    {
        private readonly SettingsService _settings;

        public bool ConfirmBeforeHide { get; set; }
        public bool ShowHiddenFiles { get; set; }

        public SettingsViewModel(SettingsService settings)
        {
            _settings = settings;
            DisplayName = "设置";
        }

        protected override async Task OnActivateAsync(CancellationToken cancellationToken)
        {
            ConfirmBeforeHide = _settings.ConfirmBeforeHide;
            ShowHiddenFiles = _settings.ShowHiddenFiles;
            await base.OnActivateAsync(cancellationToken);
        }

        public void Save()
        {
            _settings.ConfirmBeforeHide = ConfirmBeforeHide;
            _settings.ShowHiddenFiles = ShowHiddenFiles;

            if (!_settings.Save())
                MessageBox.Show("设置保存失败，详见日志：" + LogService.Directory, "HideMe",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
