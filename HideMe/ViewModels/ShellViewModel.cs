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
        public EncryptedListViewModel EncryptedList { get; }
        public SettingsViewModel Settings { get; }

        public ShellViewModel(BrowserViewModel browser,
            EncryptedListViewModel encryptedList, SettingsViewModel settings)
        {
            DisplayName = "HideMe";
            Browser = browser;
            EncryptedList = encryptedList;
            Settings = settings;
        }

        protected override async Task OnInitializeAsync(CancellationToken cancellationToken)
        {
            await ActivateItemAsync(Browser, cancellationToken);
            await base.OnInitializeAsync(cancellationToken);
        }

        public Task GoBrowser() => ActivateItemAsync(Browser);
        public Task GoEncryptedList() => ActivateItemAsync(EncryptedList);
        public Task GoSettings() => ActivateItemAsync(Settings);
    }
}
