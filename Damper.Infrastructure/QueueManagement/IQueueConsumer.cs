using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Damper.Infrastructure.QueueManagement
{
    public interface IQueueConsumer
    {
        string Consume();
    }
}