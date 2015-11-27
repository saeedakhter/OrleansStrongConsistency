using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GrainInterfaces;
using Orleans.Providers;

namespace GrainCollection
{
    public class EmployeeState : GrainState
    {
        public int Currency { get; set; }
        public int Goods { get; set; }
        public List<double> TransferTimestamps { get; set; }
        public ITransferReceiver TransferTarget { get; set; }
        public double TransferTimestamp { get; set; }
        public int TransferCurrency { get; set; }
        public int TransferGoods { get; set; }
        public int DebugTransferDelay { get; set; }
    }

    [StorageProvider(ProviderName = "AzureStorage")]
    public class Employee : Grain<EmployeeState>, IEmployee, ITransferReceiver
    {
        public override Task OnActivateAsync()
        {
            if (State.TransferTimestamps == null)
            {
                State.TransferTimestamps = new List<double>();
            }
            return base.OnActivateAsync();
        }

        // idempotent - this operation can be safely executed more than once with the same timestamp
        public async Task<bool> TransferTo(double Timestamp, ITransferReceiver target, int Currency, int Goods)
        {
            Console.WriteLine("Employee {0} is trading {1} currency for {2} goods ", this.GetPrimaryKey(), Currency, Goods);

            if (State.TransferTarget != null)
            {
                await CompleteExistingTransfer();
            }
            if (State.TransferTimestamps.Contains(Timestamp))
            {
                return true;
            }
            if (!ValidateAmounts(Currency, Goods))
            {
                return false;
            }
            State.Currency -= Currency;
            State.Goods -= Goods;
            State.TransferCurrency = Currency;
            State.TransferGoods = Goods;
            State.TransferTarget = target;
            State.TransferTimestamp = Timestamp;
            // TODO: the following line could optimized by implementing AddOrReplaceOldTimestamp() - no sense in growing the list if there is an old timestamp we don't need anymore
            State.TransferTimestamps.Add(Timestamp);
            await base.WriteStateAsync();
            if (State.DebugTransferDelay > 0)
            {
                Console.WriteLine("Debug ReceiveTransfer Wait for {0}", State.DebugTransferDelay);
                await Task.Delay(State.DebugTransferDelay);
            }
            return await CompleteExistingTransfer();          
        }

        // idempotent - this operation can be safely executed more than once with the same timestamp
        public async Task<bool> ReceiveTransfer(double Timestamp, int Currency, int Goods)
        {
            // if there is a pending transfer to another grain then better complete it or rollback
            // before allowing an incoming transfer
            if (State.TransferTarget != null)
            {
                await CompleteExistingTransfer();
            }
            if (State.TransferTimestamps.Contains(Timestamp))
            {
                Console.WriteLine("Employee {0} has already completed transaction {1}", this.GetPrimaryKey(), Timestamp);
                return true;
            }
            if (!ValidateAmounts(Currency, Goods))
            {
                return false;
            }
            State.Currency -= Currency;
            State.Goods -= Goods;
            // TODO: the following line could optimized by implementing AddOrReplaceOldTimestamp() - no sense in growing the list if there is an old timestamp we don't need anymore
            State.TransferTimestamps.Add(Timestamp);
            await base.WriteStateAsync();
            return true;
        }

        // TODO: this should be scheduled daily perhaps using a reminder
        public async Task CleanupOldTimestamps()
        {
            if (State.TransferTarget != null)
            {
                await CompleteExistingTransfer();
            }
            bool persistNeeded = false;
            double twodaysago = DateTime.Now.AddDays(-2).ToOADate();
            for (int i = State.TransferTimestamps.Count - 1; i >= 0; i--)
            {
                if (State.TransferTimestamps[i] < twodaysago)
                {
                    State.TransferTimestamps.RemoveAt(i);
                    persistNeeded = true;
                }
            }
            if (persistNeeded)
            {
                await base.WriteStateAsync();
            }
        }

        private async Task<bool> CompleteExistingTransfer()
        {
            if (State.TransferTarget != null)
            {
                bool success = await State.TransferTarget.ReceiveTransfer(
                    State.TransferTimestamp, 
                    -State.TransferCurrency, 
                    -State.TransferGoods);
                State.TransferTarget = null;
                if (!success)
                {
                    // rollback
                    Console.WriteLine("Rolling back the transaction because the receiver has insufficient balance.");
                    State.Currency += State.TransferCurrency;
                    State.Goods += State.TransferGoods;
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

        private bool ValidateAmounts(int Currency, int Goods)
        {
            if (Currency > 0 && Currency > State.Currency)
            {
                return false;
            }
            else if (Goods > 0 && Goods > State.Goods)
            {
                return false;
            }
            return true;
        }

        public Task Print()
        {
            if(State.TransferTarget==null)
            {
                Console.WriteLine("Employee {0} has Currency={1} and Goods={2}", this.GetPrimaryKey(), State.Currency, State.Goods);
            }
            else
            {
                Console.WriteLine("Employee {0} has Currency={1} and Goods={2} with pending transaction of Currency={3} Goods={4}", this.GetPrimaryKey(), State.Currency, State.Goods, State.TransferCurrency, State.TransferGoods);
            }
            return TaskDone.Done;
        }

        public Task AddGoods(int goods)
        {
            if(goods > 0)
            {
                State.Goods += goods;
                return base.WriteStateAsync();
            }
            return TaskDone.Done;
        }

        public Task AddCurrency(int currency)
        {
            if(currency > 0)
            {
                State.Currency += currency;
                return base.WriteStateAsync();
            }
            return TaskDone.Done;
        }

        public async Task<bool> SpendCurrency(int currency)
        {
            if (State.TransferTarget != null)
            {
                await CompleteExistingTransfer();
            }
            if (currency < 0 || currency > State.Currency)
            {
                return false;
            }
            Console.WriteLine("Employee {0} is spending {1} currency", this.GetPrimaryKey(), currency);
            State.Currency -= currency;
            await base.WriteStateAsync();
            return true;
        }

        public async Task<bool> SpendGoods(int goods)
        {
            if (State.TransferTarget != null)
            {
                await CompleteExistingTransfer();
            }
            if (goods < 0 || goods > State.Goods)
            {
                return false;
            }
            Console.WriteLine("Employee {0} is spending {1} goods", this.GetPrimaryKey(), goods);
            State.Goods -= goods;
            await base.WriteStateAsync();
            return true;
        }

        public Task DebugDelay(int milliseconds)
        {
            State.DebugTransferDelay = milliseconds;
            return TaskDone.Done;
        }
    }
}
