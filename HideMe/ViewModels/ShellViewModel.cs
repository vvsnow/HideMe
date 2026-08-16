using Caliburn.Micro;
using PropertyChanged;

namespace HideMe.ViewModels
{
    /// <summary>
    /// 主窗口视图模型，与 Views/ShellView 通过命名空间约定配对。
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class ShellViewModel : Screen
    {
        public ShellViewModel()
        {
            DisplayName = "HideMe";
        }
    }
}
