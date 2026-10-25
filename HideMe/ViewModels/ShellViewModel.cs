using System.Collections.ObjectModel;
using Caliburn.Micro;
using HideMe.Models;
using LightUi;

namespace HideMe.ViewModels
{
    /// <summary>
    /// 主窗口：左侧列表导航（Conductor OneActive），右侧显示当前页。
    /// </summary>
    public class ShellViewModel : Conductor<IScreen>.Collection.OneActive
    {
        public BrowserViewModel Browser { get; }
        public HiddenListViewModel HiddenList { get; }
        public SettingsViewModel Settings { get; }

        public ObservableCollection<NavItem> NavItems { get; } = new ObservableCollection<NavItem>();

        public ShellViewModel(BrowserViewModel browser,
            HiddenListViewModel hiddenList, SettingsViewModel settings)
        {
            DisplayName = "HideMe";
            Browser = browser;
            HiddenList = hiddenList;
            Settings = settings;

            NavItems.Add(new NavItem("文件浏览器", browser));
            NavItems.Add(new NavItem("隐藏列表", hiddenList));
            NavItems.Add(new NavItem("设置", settings));
        }

        protected override void OnViewLoaded(object view)
        {
            base.OnViewLoaded(view);
            ActiveItem = NavItems[0].ViewModel;
        }

        /// <summary>在 Light / Dark 主题之间切换。</summary>
        public void ToggleTheme()
        {
            ThemeManager.ApplyTheme(
                ThemeManager.Current.CurrentTheme == ApplicationTheme.Dark
                    ? ApplicationTheme.Light
                    : ApplicationTheme.Dark);
        }
    }
}
