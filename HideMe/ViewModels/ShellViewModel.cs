using System.Threading;
using System.Threading.Tasks;
using Caliburn.Micro;

namespace HideMe.ViewModels
{
    /// <summary>
    /// 主窗口：三页导航（Conductor OneActive），左侧按钮切换，右侧显示当前页。
    /// </summary>
    public class ShellViewModel : Conductor<Screen>.Collection.OneActive
    {
        public BrowserViewModel Browser { get; }
        public HiddenListViewModel HiddenList { get; }
        public SettingsViewModel Settings { get; }

        public ShellViewModel(BrowserViewModel browser,
            HiddenListViewModel hiddenList, SettingsViewModel settings)
        {
            DisplayName = "HideMe";
            Browser = browser;
            HiddenList = hiddenList;
            Settings = settings;
        }

        protected override async Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            await ActivateItemAsync(Browser, cancellationToken);
            await base.OnInitializeAsync(cancellationToken);
        }

        public Task GoBrowser() => ActivateItemAsync(Browser);
        public Task GoHiddenList() => ActivateItemAsync(HiddenList);
        public Task GoSettings() => ActivateItemAsync(Settings);
    }
}
