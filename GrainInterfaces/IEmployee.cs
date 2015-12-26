using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GrainInterfaces
{
    public class EmployeeStateTransfer
    {
        public int Currency { get; set; }
        public int Goods { get; set; }
    }

    public interface IEmployee : Orleans.IGrainWithGuidKey, IAcceptsDeltaState<EmployeeStateTransfer>
    {
        Task AddCurrency(int currency);
        Task AddGoods(int goods);

        Task<bool> SpendCurrency(int currency);
        Task<bool> SpendGoods(int goods);

        Task Print();
    }
}
