﻿using Consul;
using Daily.Carp.Configuration;
using Daily.Carp.Feature;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Newtonsoft.Json;
using Timer = System.Timers.Timer;

namespace Daily.Carp.Provider.Consul
{
    internal class ConsulCarpConfigurationProvider : BaseCarpConfigurationProvider
    {
        private ConsulRegistryConfiguration _config;
        private static readonly object lock_obj = new object();

        public ConsulCarpConfigurationProvider()
        {
            AddService(service =>
            {
                var carpConfigConsul = CarpApp.GetCarpConfig().Consul;

                _config = new ConsulRegistryConfiguration(carpConfigConsul.Protocol, carpConfigConsul.Host,
                    carpConfigConsul.Port, "", carpConfigConsul.Token);
                service.AddSingleton<IConsulClientFactory>(new ConsulClientFactory(_config));
            });
        }

        public override void Initialize()
        {
            Refresh(GetServices);
            TimingUpdate();
        }
        //为了防止其他状况 1分钟同步一次配置
        private void TimingUpdate()
        {
            Task.Run(() =>
            {
                var timer = new Timer();
                timer.Interval = 3 * 1000;
                timer.Elapsed += (sender, eventArgs) => { Refresh(GetServices); };
                timer.Start();
            });
        }
        private List<Service> GetServices(string serviceName, IServiceProvider provider)
        {
            lock (lock_obj)
            {
                var services = new List<Service>();
                var client = provider.GetService<IConsulClientFactory>()?.Get();
                var queryResult = client?.Health.Service(serviceName, string.Empty, true).ConfigureAwait(true)
                    .GetAwaiter().GetResult();
                foreach (var serviceEntry in queryResult.Response)
                {
                    try
                    {
                        if (IsValid(serviceEntry))
                        {
                            var nodes = client?.Catalog.Nodes().ConfigureAwait(true).GetAwaiter().GetResult();
                            if (nodes?.Response == null)
                            {
                                services.Add(BuildService(serviceEntry, null));
                            }
                            else
                            {
                                var serviceNode =
                                    nodes.Response.FirstOrDefault(n => n.Address == serviceEntry.Service.Address);
                                services.Add(BuildService(serviceEntry, serviceNode));
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        continue;
                    }
                }

                return services;
            }
        }

        private Service BuildService(ServiceEntry serviceEntry, Node serviceNode)
        {
            
            var services = new Service();

            services.Host = serviceNode == null ? serviceEntry.Service.Address : serviceNode.Name;

            services.Port = serviceEntry.Service.Port;

            return services;
        }

        private bool IsValid(ServiceEntry serviceEntry)
        {
            if (string.IsNullOrEmpty(serviceEntry.Service.Address) ||
                serviceEntry.Service.Address.Contains("http://") || serviceEntry.Service.Address.Contains("https://") ||
                serviceEntry.Service.Port <= 0)
            {
                return false;
            }

            return true;
        }
    }
}