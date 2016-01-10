using Orleans;
using Orleans.Concurrency;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrainInterfaces
{
    public interface ITransactionGrain<TDeltaState> : IGrainWithIntegerKey
    {
        Task<bool> Transact(GrainTransactionData<TDeltaState> transaction, int index);
    }

    [Immutable]
    public class GrainTransactionData<TDeltaState>
    {
        public const int TRANSACTION_RETRYABLE_MIN = 10;

        public Guid TransactionId { get; set; }
        public IList<Tuple<ITransactionGrain<TDeltaState>, TDeltaState>> Values { get; set; }
    }

    /// <summary>
    /// This class helps you build a transaction that will execute in the correct order, this is important to avoid cycles from forming
    /// when more than one transaction is attempting to lock all the resources necessary to complete the transaction
    /// </summary>
    public class TransactionHelper<TDeltaState>
    {
        Guid transactionId = default(Guid);
        private DateTime ExpireTimestamp;
        SortedDictionary<Guid, Tuple<ITransactionGrain<TDeltaState>, TDeltaState>> sortedByGuid = new SortedDictionary<Guid, Tuple<ITransactionGrain<TDeltaState>, TDeltaState>>();

        public TransactionHelper()
        {
            transactionId = default(Guid);
        }

        public void Add(ITransactionGrain<TDeltaState> target, TDeltaState deltaState)
        {
            Guid targetId = target.GetPrimaryKey();
            sortedByGuid.Add(targetId, new Tuple<ITransactionGrain<TDeltaState>, TDeltaState>(target, deltaState));
        }

        public async Task<bool> Execute()
        {
            if(sortedByGuid.Count < 2)
            {
                // to perform a transaction, there must be at least two items added
                return false;
            }
            if(transactionId == default(Guid))
            {
                transactionId = Guid.NewGuid();
                ExpireTimestamp = DateTime.Now.AddMinutes(TransactionStatusPoolHelper.PURGE_PERIOD_MINUTES);
            }
            else if(DateTime.Now < ExpireTimestamp)
            {
                var transactionStatus = GrainClient.GrainFactory.GetGrain<ITransactionStatusPool>(TransactionStatusPoolHelper.GetHash(transactionId));
                if(await transactionStatus.IsComplete(transactionId))
                {
                    // we know the transaction completed, don't bother retrying
                    return true;
                }
            }
            else
            {
                // perhaps throw an exception instead?  Transaction has expired and cannot be retried.
                return false;
            }

            // avoid deadlocks by sorting grains in a deterministic fashion
            var transactionitems = new List<Tuple<ITransactionGrain<TDeltaState>, TDeltaState>>();
            foreach (var item in sortedByGuid.Values)
            {
                transactionitems.Add(item);
            }
            var transaction = new GrainTransactionData<TDeltaState>()
            {
                TransactionId = transactionId,
                Values = transactionitems
            };
            ITransactionGrain<TDeltaState> firstgrain = transaction.Values[0].Item1;
            return await firstgrain.Transact(transaction, 0);
        }
    }
}
