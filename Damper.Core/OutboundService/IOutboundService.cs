using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Damper.Core.OutboundService
{
    public interface IOutboundService
    {
        Task DeliverToCustomerAsync(string rabbitMqMessage);
    }
}