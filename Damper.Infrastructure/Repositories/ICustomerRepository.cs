using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Damper.Infrastructure.Repositories
{
    public interface ICustomerRepository
    {
        Task<CustomerConfig?> GetByIdAsync(string customerId);
    }
}