using GrainInterfaces;
using Orleans;
using Orleans.Runtime.Host;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Host
{
    class Program
    {
        static SiloHost siloHost;

        static void Main(string[] args)
        {
            // Orleans runs in it's own AppDomain
            AppDomain hostDomain = AppDomain.CreateDomain("OrleansHost", null,
                new AppDomainSetup()
                {
                    AppDomainInitializer = InitSilo
                });

            Console.WriteLine("Orleans Silo is running.");
            Task t = DoClientWork();
            t.Wait();

            Console.WriteLine("Client work complete.  Press [ENTER] to terminate...");
            Console.ReadLine();

            hostDomain.DoCallBack(ShutdownSilo);
        }

        static async Task DoClientWork()
        {
            // basic configuration
            var clientconfig = new Orleans.Runtime.Configuration.ClientConfiguration();
            clientconfig.Gateways.Add(new IPEndPoint(IPAddress.Loopback, 30000));

            GrainClient.Initialize(clientconfig);

            var idE1 = "5ad92744-a0b1-487b-a9e7-e6b91e9a9826";
            var idE2 = "2eef0ac5-540f-4421-b9a9-79d89400f7ab";
            var e1 = GrainClient.GrainFactory.GetGrain<IEmployee>(Guid.Parse(idE1));
            var e2 = GrainClient.GrainFactory.GetGrain<IEmployee>(Guid.Parse(idE2));

            Console.WriteLine("Current State:");
            await e1.Print();
            await e2.Print();

            if (!await e1.SpendCurrency(10))
            {
                Console.WriteLine("Employee {0} does not have 10 currency to spend", idE1);
            }

            if (!await e2.SpendCurrency(10))
            {
                Console.WriteLine("Employee {0} does not have 10 currency to spend", idE2);
            }

            await e1.AddCurrency(20);
            await e2.AddGoods(10);

            await e1.DebugDelay(4000);

            await e1.Print();
            await e2.Print();

            await e1.TransferTo(DateTime.Now.ToOADate(), e2, 5, -5);

            await e1.Print();
            await e2.Print();

            await e1.TransferTo(DateTime.Now.ToOADate(), e2, 5, -500);

            await e1.Print();
            await e2.Print();

            await e1.TransferTo(DateTime.Now.ToOADate(), e2, 5, -5);

            await e1.Print();
            await e2.Print();

            Console.WriteLine("Done!");
        }

        static void InitSilo(string[] args)
        {
            siloHost = new SiloHost(System.Net.Dns.GetHostName());
            // possible from code, but easier via config
            siloHost.ConfigFileName = "OrleansConfiguration.xml";

            siloHost.InitializeOrleansSilo();
            var startedok = siloHost.StartOrleansSilo();
            if (!startedok)
            {
                throw new SystemException(String.Format("Failed to start silo '{0}' as '{1}' node", siloHost.Name, siloHost.Type));
            }
        }

        static void ShutdownSilo()
        {
            if (siloHost != null)
            {
                siloHost.Dispose();
                GC.SuppressFinalize(siloHost);
                siloHost = null;
            }
        }
    }

}
