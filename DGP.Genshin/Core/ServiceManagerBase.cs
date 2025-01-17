﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Toolkit.Mvvm.Messaging;
using Snap.Core.DependencyInjection;
using Snap.Exception;
using System;
using System.Reflection;

namespace DGP.Genshin.Core
{
    /// <summary>
    /// 服务管理器
    /// 依赖注入的核心管理类
    /// </summary>
    internal class ServiceManagerBase
    {
        /// <summary>
        /// 实例化一个新的服务管理器
        /// </summary>
        public ServiceManagerBase()
        {
            Services = ConfigureServices();
        }
        /// <summary>
        /// 获取 <see cref="IServiceProvider"/> 的实例
        /// 存放类
        /// </summary>
        public IServiceProvider? Services { get; protected init; }

        /// <summary>
        /// 向容器注册服务
        /// </summary>
        /// <param name="services">容器</param>
        /// <param name="type">待检测的类型</param>
        /// <exception cref="SnapGenshinInternalException">未知的注册类型</exception>
        private void RegisterService(ServiceCollection services, Type type)
        {
            //注册服务类型
            if (type.GetCustomAttribute<ServiceAttribute>() is ServiceAttribute serviceAttr)
            {
                _ = serviceAttr.InjectAs switch
                {
                    InjectAs.Singleton => services.AddSingleton(serviceAttr.InterfaceType, type),
                    InjectAs.Transient => services.AddTransient(serviceAttr.InterfaceType, type),
                    _ => throw new SnapGenshinInternalException($"未知的服务类型 {type}"),
                };
            }
            //注册视图模型
            if (type.GetCustomAttribute<ViewModelAttribute>() is ViewModelAttribute viewModelAttr)
            {
                _ = viewModelAttr.InjectAs switch
                {
                    InjectAs.Singleton => services.AddSingleton(type),
                    InjectAs.Transient => services.AddTransient(type),
                    _ => throw new SnapGenshinInternalException($"未知的视图模型类型 {type}"),
                };
            }
            //注册视图
            if (type.GetCustomAttribute<ViewAttribute>() is ViewAttribute viewAttr)
            {
                _ = viewAttr.InjectAs switch
                {
                    InjectAs.Singleton => services.AddSingleton(type),
                    InjectAs.Transient => services.AddTransient(type),
                    _ => throw new SnapGenshinInternalException($"未知的视图类型 {type}"),
                };
            }
        }

        /// <summary>
        /// 向容器注册服务
        /// </summary>
        /// <param name="services">容器</param>
        /// <param name="entryType">入口类型，该类型所在的程序集均会被扫描</param>
        /// <exception cref="SnapGenshinInternalException">注册的类型为非已知类型</exception>
        protected void RegisterServices(ServiceCollection services, Type entryType)
        {
            foreach (Type type in entryType.Assembly.GetTypes())
            {
                RegisterService(services, type);
            }
        }

        /// <summary>
        /// 探测服务
        /// 并向容器添加默认的 <see cref="IMessenger"/> 与 服务
        /// </summary>
        /// <param name="services"></param>
        protected virtual void OnProbingServices(ServiceCollection services)
        {
            //register default services
            RegisterServices(services, typeof(App));
        }

        /// <summary>
        /// 配置服务
        /// 探测服务
        /// <see cref="OnProbingServices(ServiceCollection)"/>会被调用以探测整个程序集
        /// 一旦服务配置完成，就无法继续注册
        /// </summary>
        protected IServiceProvider ConfigureServices()
        {
            ServiceCollection services = new();
            OnProbingServices(services);
            return services.BuildServiceProvider();
        }
    }
}
