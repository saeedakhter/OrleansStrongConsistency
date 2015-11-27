using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrainInterfaces
{
    // can receive a transfer - Employee is the only grain implementing this interface in this example, but it could make sense for many grain types to implement this
    public interface ITransferReceiver
    {
        // idempotent - this operation can be safely executed more than once with the same timestamp
        Task<bool> ReceiveTransfer(double Timestamp, int Currency, int Goods);
    }

    public interface IEmployee : Orleans.IGrainWithGuidKey, ITransferReceiver
    {
        Task<bool> TransferTo(double Timestamp, ITransferReceiver target, int Currency, int Goods);

        Task AddCurrency(int currency);
        Task AddGoods(int goods);

        Task<bool> SpendCurrency(int currency);
        Task<bool> SpendGoods(int goods);

        Task Print();
        Task DebugDelay(int milliseconds);

    }
}
