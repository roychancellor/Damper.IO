using System.Threading.Channels;
using Damper.Infrastructure.Models;

namespace Damper.Infrastructure.CustomerChannels
{
    public class CustomerEgressPipeline
    {
        public ChannelWriter<WebhookEnvelope> Writer { get; }
        public Task BackgroundTask { get; }

        public CustomerEgressPipeline(ChannelWriter<WebhookEnvelope> writer, Task backgroundTask)
        {
            Writer = writer;
            BackgroundTask = backgroundTask;
        }
    }
}