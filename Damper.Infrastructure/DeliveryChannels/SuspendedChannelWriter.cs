using System.Threading.Channels;
using Damper.Infrastructure.MessageTransport;

namespace Damper.Infrastructure.DeliveryChannels
{
    public class SuspendedChannelWriter : ChannelWriter<MessageEnvelope>
    {
        // Always returns false instantly to signal to the Shard Worker that it cannot take the message
        public override bool TryWrite(MessageEnvelope item) => false;

        public override ValueTask<bool> WaitToWriteAsync(CancellationToken ct) => ValueTask.FromResult(false);
    }
}