using GrainInterfaces;
using Orleans;
using System;
using System.Threading.Tasks;
using Orleans.Runtime;

namespace GrainCollection
{
    public class TransactionGrainState<TDeltaState> : GrainState
    {
        public IGrainReminder Reminder { get; set; }
        public Guid TransactionId { get; set; }
        public TDeltaState TransactionDelta { get; set; }
    }

    public abstract class TransactionGrain<TGrainState, TDeltaState> : Orleans.Grain<TGrainState>, ITransactionGrain<TDeltaState>, IRemindable
        where TGrainState : TransactionGrainState<TDeltaState>, new()
    {
        private double TransferTimestamp;
        private bool IsTransactionPersistenceRequired;

        /// <summary>
        /// Implement this function to increase properties such as Currency, Goods, and any other delta to your current state
        /// this is called when there is an incoming Transfer
        /// </summary>
        protected abstract bool Commit(TDeltaState deltaState);

        /// <summary>
        /// Implement this function to deduct properties such as Currency, Goods, and any other delta to your current state
        /// this is called when there is a rollback
        /// </summary>
        protected abstract bool Rollback(TDeltaState deltaState);

        public override async Task OnActivateAsync()
        {
            await CheckForPendingTransaction();
            await base.OnActivateAsync();
        }

        protected new Task WriteStateAsync()
        {
            IsTransactionPersistenceRequired = false;
            return base.WriteStateAsync();
        }

        private async Task CheckForPendingTransaction()
        {
            if (State.TransactionId != default(Guid))
            {
                var transactionStatus = GrainFactory.GetGrain<ITransactionStatusPool>(TransactionStatusPoolHelper.GetHash(State.TransactionId));
                bool success = await transactionStatus.IsComplete(State.TransactionId);
                if (success)
                {
                    State.TransactionId = default(Guid);
                }
                else
                {
                    Console.WriteLine("Rolling back the transaction because it never completed.");
                    Rollback(State.TransactionDelta);
                    State.TransactionId = default(Guid);
                }
            }
        }

        public async Task<bool> Transact(GrainTransactionData<TDeltaState> transaction, int index)
        {
            // setting TransactionId locks record as pending, on silo failure OnActivateAsync recovers it
            State.TransactionId = transaction.TransactionId;
            State.TransactionDelta = transaction.Values[index].Item2;
            TransferTimestamp = DateTime.Now.ToOADate();
            if(!Commit(State.TransactionDelta))
            {
                return false;
            }
            if (State.Reminder == null)
            {
                // the reminder ensures that recovery will occur on silo failure
                // once a reminder is set, it can be reused for multiple transactions
                string name = "TransactionGrain" + this.GetPrimaryKey();
                // make sure the reminder occurs before the TransactionStatusPool purges status for this transaction
                TimeSpan WAKEUP_PERIOD = TimeSpan.FromMinutes(TransactionStatusPoolHelper.PURGE_PERIOD_MINUTES / 2);
                State.Reminder = await this.RegisterOrUpdateReminder(name, WAKEUP_PERIOD, WAKEUP_PERIOD);
            }
            await base.WriteStateAsync();
            int nextindex = index + 1;
            if (nextindex >= transaction.Values.Count)
            {
                // we have reached the end of the transaction, record that it's complete then unwind the stack
                var transactionStatus = GrainFactory.GetGrain<ITransactionStatusPool>(TransactionStatusPoolHelper.GetHash(State.TransactionId));
                await transactionStatus.SetComplete(State.TransactionId);
            }
            else
            {
                bool exceptionThrown = false;
                try
                {
                    if (!await transaction.Values[nextindex].Item1.Transact(transaction, nextindex))
                    {
                        // a member considers this transaction illegal, rollback
                        Rollback(State.TransactionDelta);
                        State.TransactionId = default(Guid);
                        return false;
                    }
                }
                catch
                {
                    exceptionThrown = true;
                }

                if (exceptionThrown)
                {
                    // if an exception is thrown we are left in a state where it is unclear if the transaction completed
                    // use the TransactionStatus grain to ensure that all grains either commit or roll back atomically
                    await CheckForPendingTransaction();
                }
            }

            // clear transaction delay persisting for performance till ReceiveReminder
            // OnActivateAsync will check TransactionStatus on silo crash
            IsTransactionPersistenceRequired = true;
            State.TransactionId = default(Guid);
            return true;
        }

        // if the state has not been persisted do it now, OnActivateAsync ensures that the transaction status is resolved
        public async Task ReceiveReminder(string reminderName, TickStatus status)
        {
            if(IsTransactionPersistenceRequired)
            {
                IsTransactionPersistenceRequired = false;
                await base.WriteStateAsync();
            }
            else
            {
                // persist cleared state first, if a failure occurs before unregistering, it's OK to register a reminder twice
                IGrainReminder reminder = State.Reminder;
                State.Reminder = null;
                await base.WriteStateAsync();
                // if it's an inactive grain, let's cancel the reminder
                await UnregisterReminder(reminder);
            }
        }
    }
}
