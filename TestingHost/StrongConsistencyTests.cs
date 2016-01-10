using Microsoft.VisualStudio.TestTools.UnitTesting;
using Orleans.TestingHost;
using System;
using System.Threading.Tasks;
using GrainInterfaces;

namespace TestingHost
{
    [TestClass]
    public class StrongConsistencyTests : TestingSiloHost
    {
        [ClassCleanup]
        public static void ClassCleanup()
        {
            StopAllSilos();
        }

        [TestMethod]
        public async Task TradeGoodsForCurrencyTest()
        {
            var e1 = GrainFactory.GetGrain<IEmployee>(Guid.NewGuid());
            var e2 = GrainFactory.GetGrain<IEmployee>(Guid.NewGuid());
            await e1.AddCurrency(5);
            await e2.AddGoods(5);

            var transaction = new TransactionHelper<EmployeeStateTransfer>();
            transaction.Add(e1, new EmployeeStateTransfer() { Currency = -5, Goods = 5 });
            transaction.Add(e2, new EmployeeStateTransfer() { Currency = 5, Goods = -5 });
            
            // Executing transaction to trade 5 currency for 5 goods
            Assert.IsTrue(await transaction.Execute());
            Assert.AreEqual(5, await e1.GetGoods());
            Assert.AreEqual(0, await e1.GetCurrency());
            Assert.AreEqual(5, await e2.GetCurrency());
            Assert.AreEqual(0, await e2.GetGoods());
        }

        [TestMethod]
        public async Task InsufficientCurrencyTest()
        {
            var e1 = GrainFactory.GetGrain<IEmployee>(Guid.NewGuid());
            var e2 = GrainFactory.GetGrain<IEmployee>(Guid.NewGuid());
            await e1.AddCurrency(1);
            await e2.AddGoods(5);

            var transaction = new TransactionHelper<EmployeeStateTransfer>();
            transaction.Add(e1, new EmployeeStateTransfer() { Currency = -5, Goods = 5 });
            transaction.Add(e2, new EmployeeStateTransfer() { Currency = 5, Goods = -5 });

            // Executing transaction to trade 5 currency for 5 goods, but 5 Currency is not available...
            Assert.IsFalse(await transaction.Execute());
            Assert.AreEqual(1, await e1.GetCurrency());
            Assert.AreEqual(5, await e2.GetGoods());
        }

        [TestMethod]
        public async Task InsufficientGoodsTest()
        {
            var e1 = GrainFactory.GetGrain<IEmployee>(Guid.NewGuid());
            var e2 = GrainFactory.GetGrain<IEmployee>(Guid.NewGuid());
            await e1.AddCurrency(5);
            await e2.AddGoods(1);

            var transaction = new TransactionHelper<EmployeeStateTransfer>();
            transaction.Add(e1, new EmployeeStateTransfer() { Currency = -5, Goods = 5 });
            transaction.Add(e2, new EmployeeStateTransfer() { Currency = 5, Goods = -5 });

            // Executing transaction to trade 5 currency for 5 goods, but 5 Goods is not available...
            Assert.IsFalse(await transaction.Execute());
            Assert.AreEqual(5, await e1.GetCurrency());
            Assert.AreEqual(1, await e2.GetGoods());
        }

        [TestMethod]
        public async Task LessThanTwoTransactionItemsTest()
        {
            var e1 = GrainFactory.GetGrain<IEmployee>(Guid.NewGuid());
            await e1.AddCurrency(5);

            var transaction = new TransactionHelper<EmployeeStateTransfer>();
            transaction.Add(e1, new EmployeeStateTransfer() { Currency = -5, Goods = 5 });

            // Executing transaction with only one item, two or more are required.
            Assert.IsFalse(await transaction.Execute());
            Assert.AreEqual(5, await e1.GetCurrency());
            Assert.AreEqual(0, await e1.GetGoods());
        }
    }
}
