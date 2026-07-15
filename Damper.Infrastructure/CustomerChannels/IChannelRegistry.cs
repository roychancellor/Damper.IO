using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Channels;
using System.Threading.Tasks;
using Damper.Infrastructure.Models;

namespace Damper.Infrastructure.ChannelRegistry
{
    public interface IChannelRegistry
    {
        Task<ChannelWriter<WebhookEnvelope>> GetOrCreateChannel(string customerId);
    }
}