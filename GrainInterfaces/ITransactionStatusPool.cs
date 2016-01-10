using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrainInterfaces
{
    public class TransactionStatusPoolHelper
    {
        public const int PURGE_PERIOD_MINUTES = 120;

        // currently uses a pool size of 256 which should handle 2000 transactions per second
        public static int GetHash(Guid id)
        {
            byte[] bytearray = id.ToByteArray();
            return bytearray[0];
        }
    }

    public interface ITransactionStatusPool : Orleans.IGrainWithIntegerKey
    {
        Task SetComplete(Guid transactionId);
        Task<bool> IsComplete(Guid transactionId);
    }
}
