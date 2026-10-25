using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HideMe.Views
{
    /// <summary>
    /// BrowserView.xaml 的交互逻辑
    /// </summary>
    public partial class BrowserView : UserControl
    {
        public BrowserView()
        {
            InitializeComponent();
        }

        // 右键时先选中被点击的节点，菜单 action 仍由 Caliburn 绑定
        private void DirectoryTree_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
            if (item != null)
                item.IsSelected = true;
        }

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                var match = current as T;
                if (match != null) return match;

                try
                {
                    current = VisualTreeHelper.GetParent(current);
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }
    }
}
