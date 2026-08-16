using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
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
