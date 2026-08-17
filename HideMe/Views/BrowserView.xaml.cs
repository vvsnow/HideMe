using System.Windows;
using System.Windows.Controls;
using HideMe.Models;
using HideMe.ViewModels;

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

        // TreeView.SelectedItem 不可绑定，通过 code-behind 转发到 VM
        private void DirectoryTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (DataContext is BrowserViewModel vm)
                vm.SelectedNode = e.NewValue as FileSystemNode;
        }
    }
}
