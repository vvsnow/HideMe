using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Caliburn.Micro;
using HideMe.Services;
using HideMe.ViewModels;

namespace HideMe
{
    /// <summary>
    /// 应用引导程序：负责容器配置、视图模型定位、根窗口显示和全局异常处理。
    /// 由 App 构造函数实例化，应用启动时自动执行 Initialize()。
    /// </summary>
    public class Bootstrapper : BootstrapperBase
    {
        private SimpleContainer _container;

        public Bootstrapper()
        {
            Initialize();
        }

        /// <summary>
        /// 配置 IoC 容器。
        /// </summary>
        protected override void Configure()
        {
            // MenuItem 默认事件约定：支持 cal:Message.Attach="Hide" 简写
            ConventionManager.AddElementConvention<MenuItem>(MenuItem.HeaderProperty, "DataContext", "Click");

            _container = new SimpleContainer();

            // 框架服务
            _container.Singleton<IWindowManager, WindowManager>();
            _container.Singleton<IEventAggregator, EventAggregator>();

            // 业务服务
            _container.Singleton<SettingsService>();

            // 根视图模型（单例，整个应用共享一个主窗口实例）
            _container.Singleton<ShellViewModel>();

            // 自动注册 ViewModels 命名空间下的其余视图模型
            foreach (var type in Assembly.GetExecutingAssembly()
                         .GetTypes()
                         .Where(t => t.IsClass && !t.IsAbstract
                                  && t.Namespace == "HideMe.ViewModels"
                                  && t != typeof(ShellViewModel)))
            {
                _container.RegisterPerRequest(type, type.ToString(), type);
            }
        }

        protected override object GetInstance(Type service, string key)
        {
            return _container.GetInstance(service, key);
        }

        protected override IEnumerable<object> GetAllInstances(Type service)
        {
            return _container.GetAllInstances(service);
        }

        protected override void BuildUp(object instance)
        {
            _container.BuildUp(instance);
        }

        protected override void OnStartup(object sender, StartupEventArgs e)
        {
            LogService.CleanupOldLogs();
            DisplayRootViewForAsync<ShellViewModel>();
        }

        protected override void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogService.Error("未处理的异常", e.Exception);
            MessageBox.Show("发生未处理异常，程序将继续运行。\r\n详情已写入日志：\r\n" + LogService.Directory,
                "HideMe", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
