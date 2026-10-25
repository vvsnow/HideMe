using System.Windows;

namespace HideMe
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // 在此实例化引导程序：Bootstrapper 构造函数里订阅 Application.Startup，
            // 因此能在 Run() 触发 Startup 事件时执行 OnStartup 并显示主窗口。
            new Bootstrapper();
        }
    }
}
