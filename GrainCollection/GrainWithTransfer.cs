using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Providers;
using GrainInterfaces;

namespace GrainCollection
{
    public class GrainStateWithTransfer<TDeltaState> : GrainState
    {
        public Queue<double> TransferTimestamps { get; set; }
        public Queue<Guid> TransferIds { get; set; }
        public IAcceptsDeltaState<TDeltaState> TransferTarget { get; set; }
        public double TransferTimestamp { get; set; }
        public Guid TransferId { get; set; }
        public TDeltaState TransferInFlight { get; set; }
        public double CleanupTimestamp { get; set; }
    }

    [StorageProvider(ProviderName = "MemoryStore")]
    public abstract class GrainWithTransfer<TGrainState, TDeltaState> : Orleans.Grain<TGrainState>, IAcceptsDeltaState<TDeltaState>, IRequiresCleanup
        where TGrainState : GrainStateWithTransfer<TDeltaState>, new()
    {
        HashSet<Guid> TransferIdFastLookup;

        public override async Task OnActivateAsync()
        {
            TransferIdFastLookup = new HashSet<Guid>();
            if (State.TransferTimestamps == null)
            {
                State.TransferTimestamps = new Queue<double>();
            }
            if (State.TransferIds == null)
            {
                State.TransferIds = new Queue<Guid>();
            }

            RemoveOldTranscations();
            foreach(Guid id in State.TransferIds.ToArray())
            {
                TransferIdFastLookup.Add(id);
            }
            await CompleteExistingTransfer();
            await base.OnActivateAsync();
        }

        private void AddTransaction(Guid transactionId)
        {
            TransferIdFastLookup.Add(transactionId);
            State.TransferIds.Enqueue(transactionId);
            State.TransferTimestamps.Enqueue(State.TransferTimestamp);
        }

        private void RemoveOldTranscations()
        {
            bool IsTransferIdLookupPopulated = TransferIdFastLookup.Count > 0;
            double cutoff = DateTime.Now.AddHours(-12).ToOADate();
            while(State.TransferTimestamps.Count > 0 && State.TransferTimestamps.Peek() < cutoff)
            {
                State.TransferTimestamps.Dequeue();
                Guid id = State.TransferIds.Dequeue();
                if(IsTransferIdLookupPopulated)
                {
                    TransferIdFastLookup.Remove(id);
                }
            }
        }

        /// <summary>
        /// Implement this function to deduct properties such as Currency, Goods, and any other delta to your current state
        /// this is called when there is an outgoing Transfer
        /// </summary>
        protected abstract bool Send(TDeltaState deltaState);

        /// <summary>
        /// Implement this function to increase properties such as Currency, Goods, and any other delta to your current state
        /// this is called when there is an incoming Transfer, or when a failed outgoing transfer is rolled back 
        /// </summary>
        protected abstract bool Receive(TDeltaState deltaState);

        public async Task<bool> Transfer(Guid transactionId, IAcceptsDeltaState<TDeltaState> target, TDeltaState delta)
        {
            if (State.TransferTarget != null)
            {
                await CompleteExistingTransfer();
            }
            if (TransferIdFastLookup.Contains(transactionId))
            {
                return true;
            }
            // if a Cleanup was requested more than 90 minutes ago, request another one
            double cutoff = DateTime.Now.AddMinutes(-90).ToOADate();
            if(State.CleanupTimestamp < cutoff)
            {
                State.CleanupTimestamp = DateTime.Now.ToOADate();
                var grain = GrainFactory.GetGrain<ICleanupAgent>(0);
                await grain.RequestCleanup(this);
            }
            if (!Send(delta))
            {
                return false;
            }
            State.TransferInFlight = delta;
            State.TransferTarget = target;
            State.TransferId = transactionId;
            State.TransferTimestamp = DateTime.Now.ToOADate();
            AddTransaction(transactionId);
            await base.WriteStateAsync();
            return await CompleteExistingTransfer();
        }

        private async Task<bool> CompleteExistingTransfer()
        {
            if (State.TransferTarget != null)
            {
                bool success = await State.TransferTarget.Receive(State.TransferId, State.TransferInFlight);
                State.TransferTarget = null;
                if (!success)
                {
                    // rollback
                    Console.WriteLine("Rolling back the transaction because the receiver has insufficient balance.");
                    Receive(State.TransferInFlight);
                    return false;
                }
                // this state does not need to be persisted
                // this silo might fail anytime after the receiver has persisted anyway so we should not rely on that
                // if this silo does crash, then CompleteExistingTransfer will attempt the call to the receiver again.
                // the call to the receiver is idempotent so it will succeed again if the call was already previously successful
                // if it was not successful then we simply roll back again and we are in the same state once more
            }
            return true;
        }

        // idempotent - this operation can be safely executed more than once with the same timestamp
        public async Task<bool> Receive(Guid transactionId, TDeltaState delta)
        {
            // if there is a pending transfer to another grain then better complete it or rollback
            // before allowing an incoming transfer
            if (State.TransferTarget != null)
            {
                await CompleteExistingTransfer();
            }
            if (TransferIdFastLookup.Contains(transactionId))
            {
                Console.WriteLine("Grain {0} has already completed transaction {1}", this.GetPrimaryKey(), transactionId);
                return true;
            }
            if (!Receive(delta))
            {
                return false;
            }
            AddTransaction(transactionId);
            await base.WriteStateAsync();
            return true;
        }

        public Task Cleanup()
        {
            throw new NotImplementedException();
        }
    }
}
