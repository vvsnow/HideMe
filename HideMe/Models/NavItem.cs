using Caliburn.Micro;

namespace HideMe.Models
{
    /// <summary>
    /// 左侧导航的一项：显示标题 + 对应的页面视图模型。
    /// </summary>
    public sealed record NavItem(string Title, IScreen ViewModel);
}
