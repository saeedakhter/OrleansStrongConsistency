using GrainInterfaces;
using Orleans;
using Orleans.Concurrency;
using Orleans.Providers;
using Orleans.Runtime;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GrainCollection
{
    public class TransactionStatusPoolState : GrainState
    {
        public IGrainReminder Reminder { get; set; }
        public HashSet<Guid> SuccessfulArchive { get; set; }
        public HashSet<Guid> SuccessfulCurrent { get; set; }
    }

    [Reentrant]
    [StorageProvider(ProviderName="Default")]
    public class TransactionStatusPool : Grain<TransactionStatusPoolState>, ITransactionStatusPool, IRemindable
    {
        private Task outstandingWriteOperation;

        public override async Task OnActivateAsync()
        {
            if (State.SuccessfulArchive == null)
            {
                State.SuccessfulArchive = new HashSet<Guid>();
            }
            if (State.SuccessfulCurrent == null)
            {
                State.SuccessfulCurrent = new HashSet<Guid>();
            }
            if (State.Reminder == null)
            {
                Random rnd = new Random();
                string name = "TransactionStatusPool_" + this.GetPrimaryKey();
                int PURGE_PERIOD_MINUTES = TransactionStatusPoolHelper.PURGE_PERIOD_MINUTES;
                TimeSpan firstTime = TimeSpan.FromMinutes(PURGE_PERIOD_MINUTES + rnd.Next(1, PURGE_PERIOD_MINUTES));
                TimeSpan reoccuringTime = TimeSpan.FromMinutes(PURGE_PERIOD_MINUTES);
                State.Reminder = await this.RegisterOrUpdateReminder(name, firstTime, reoccuringTime);
                await SeralizedWriteState();
            }
            await base.OnActivateAsync();
        }

        public async Task SetComplete(Guid transactionId)
        {
            State.SuccessfulCurrent.Add(transactionId);
            await SeralizedWriteState();
        }

        public Task<bool> IsComplete(Guid transactionId)
        {
            return Task.FromResult(State.SuccessfulCurrent.Contains(transactionId) || State.SuccessfulArchive.Contains(transactionId));
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

        async Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
        {
            State.SuccessfulArchive = State.SuccessfulCurrent;
            State.SuccessfulCurrent = new HashSet<Guid>();
            await SeralizedWriteState();
        }
    }
}
