using System.Threading.Channels;
using Damper.Infrastructure.Models;

namespace Damper.Infrastructure.CustomerChannels
{
    public class SuspendedChannelWriter : ChannelWriter<WebhookEnvelope>
    {
        // Always returns false instantly to signal to the Shard Worker that it cannot take the message
        public override bool TryWrite(WebhookEnvelope item) => false;

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken ct) => ValueTask.FromResult(false);
    }
}