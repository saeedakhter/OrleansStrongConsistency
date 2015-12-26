using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrainInterfaces
{
    public interface IAcceptsDeltaState<TDeltaState> : IGrainWithIntegerKey
    {
        Task<bool> Transfer(Guid transactionId, IAcceptsDeltaState<TDeltaState> target, TDeltaState delta);
        Task<bool> Receive(Guid transactionId, TDeltaState delta);
    }
}
