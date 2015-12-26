using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrainInterfaces
{
    public interface IRequiresCleanup : IGrainWithIntegerKey
    {
        Task Cleanup();
    }

    public interface ICleanupAgent : Orleans.IGrainWithIntegerKey, IRemindable
    {
        Task RequestCleanup(IRequiresCleanup grain);
    }
}
