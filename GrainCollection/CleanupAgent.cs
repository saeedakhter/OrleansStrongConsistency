using GrainInterfaces;
using Orleans;
using Orleans.Concurrency;
using Orleans.Providers;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrainCollection
{
    public class CleanupAgentState : GrainState
    {
        public IGrainReminder Reminder { get; set; }
        public Queue<Tuple<double, IRequiresCleanup>> RequiresCleanup { get; set; }
    }

    // For high performance, use a pool of these grains:
    // var cleanupAgent = GrainClient.GrainFactory.GetGrain<ICleanupAgent>(rand.Next(0, POOL_SIZE));
    // cleanupAgent.RequestCleanup(this)
    [Reentrant]
    [StorageProvider(ProviderName = "AzureStorage")]
    public class CleanupAgent : Grain<CleanupAgentState>, ICleanupAgent, IRemindable
    {
        private Task outstandingWriteOperation;
        public const int WAKEUP_PERIOD_MINUTES = 30;
        public const int CLEANUP_AFTER_MINUTES = 90;

        public override async Task OnActivateAsync()
        {
            if (State.RequiresCleanup == null)
            {
                State.RequiresCleanup = new Queue<Tuple<double, IRequiresCleanup>>();
            }
            if (State.Reminder == null)
            {
                Random rnd = new Random();
                string name = "CleanupAgent" + this.GetPrimaryKey();
                State.Reminder = await this.RegisterOrUpdateReminder(name, TimeSpan.FromMinutes(rnd.Next(1, WAKEUP_PERIOD_MINUTES)), TimeSpan.FromHours(WAKEUP_PERIOD_MINUTES));
                await SeralizedWriteState();
            }
            await base.OnActivateAsync();
        }

        private async Task SeralizedWriteState()
        {
            // reentrant grains are still single threaded,
            // but interleaving can happen during await
            while (outstandingWriteOperation != null)
            {
                await outstandingWriteOperation;
            }
            outstandingWriteOperation = WriteStateAsync();
            await outstandingWriteOperation;
            outstandingWriteOperation = null;
        }

        public async Task RequestCleanup(IRequiresCleanup grain)
        {
            var add = new Tuple<double, IRequiresCleanup>(DateTime.Now.ToOADate(), grain);
            State.RequiresCleanup.Enqueue(add);
            await SeralizedWriteState();
        }

        async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
        {
            Tuple<double, IRequiresCleanup> next;
            double cutoff = DateTime.Now.AddMinutes(-CLEANUP_AFTER_MINUTES).ToOADate();
            while(State.RequiresCleanup.Count > 0)
            {
                next = State.RequiresCleanup.Peek();
                // We do not want to cleanup items that are recent - stop when we reach T-2 hours
                if(next.Item1 < cutoff)
                {
                    break;
                }
                next = State.RequiresCleanup.Dequeue();
                await next.Item2.Cleanup();
            }
            await SeralizedWriteState();
        }
    }
}
