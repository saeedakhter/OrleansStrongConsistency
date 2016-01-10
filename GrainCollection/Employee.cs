using Orleans;
using System;
using System.Threading.Tasks;
using GrainInterfaces;
using Orleans.Providers;

namespace GrainCollection
{
    [Serializable]
    public class EmployeeState : TransactionGrainState<EmployeeStateTransfer>
    {
        public string Name { get; set; }
        public int Currency { get; set; }
        public int Goods { get; set; }
    }

    [StorageProvider(ProviderName="Default")]
    public class Employee : TransactionGrain<EmployeeState,EmployeeStateTransfer>, IEmployee
    {
        protected override bool Commit(EmployeeStateTransfer deltaState)
        {
            if (State.Currency + deltaState.Currency < 0 || State.Goods + deltaState.Goods < 0)
            {
                Console.WriteLine("Employee.Commit returning false, insufficient funds");
                return false;
            }
            State.Currency += deltaState.Currency;
            State.Goods += deltaState.Goods;
            return true;
        }

        protected override bool Rollback(EmployeeStateTransfer deltaState)
        {
            Console.WriteLine("Employee.Rollback");
            State.Currency -= deltaState.Currency;
            State.Goods -= deltaState.Goods;
            return true;
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
            if (currency > 0)
            {
                if(currency <= State.Currency)
                {
                    Console.WriteLine("Employee {0} is spending {1} currency", this.GetPrimaryKey(), currency);
                    State.Currency -= currency;
                    await base.WriteStateAsync();
                    return true;
                }
                Console.WriteLine("Employee {0} has insufficient funds to spend {1} currency", this.GetPrimaryKey(), currency);
            }
            return false;
        }

        public async Task<bool> SpendGoods(int goods)
        {
            if (goods > 0)
            {
                if (goods <= State.Goods)
                {
                    Console.WriteLine("Employee {0} is spending {1} goods", this.GetPrimaryKey(), goods);
                    State.Goods -= goods;
                    await WriteStateAsync();
                    return true;
                }
                Console.WriteLine("Employee {0} has insufficient funds to spend {1} goods", this.GetPrimaryKey(), goods);
            }
            return false;
        }

        public Task Print()
        {
            Console.WriteLine("Employee {0} has {1} goods and {2} currency", this.GetPrimaryKey(), State.Goods, State.Currency);
            return TaskDone.Done;
        }

        public Task<int> GetCurrency()
        {
            return Task.FromResult(State.Currency);
        }

        public Task<int> GetGoods()
        {
            return Task.FromResult(State.Goods);
        }
    }
}
