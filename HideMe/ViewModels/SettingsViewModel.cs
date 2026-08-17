using System.Threading;
using System.Threading.Tasks;
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

        protected override async Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            ConfirmBeforeHide = _settings.ConfirmBeforeHide;
            ShowHiddenFiles = _settings.ShowHiddenFiles;
            await base.OnInitializeAsync(cancellationToken);
        }

        public async Task Save()
        {
            _settings.ConfirmBeforeHide = ConfirmBeforeHide;
            _settings.ShowHiddenFiles = ShowHiddenFiles;
            _settings.Save();
            await Task.CompletedTask;
        }
    }
}
