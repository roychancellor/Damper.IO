using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Damper.Infrastructure.Repositories
{
    public class PostgreSqlCustomerRepository : ICustomerRepository
    {
        public Task<CustomerConfig?> GetByIdAsync(string customerId, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}