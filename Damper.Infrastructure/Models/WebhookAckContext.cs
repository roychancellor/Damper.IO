using Damper.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace Damper.Infrastructure.Models
{
    public class WebhookAckContext
    {
        private static readonly ILogger _log = Loggers.Request;

        public ulong DeliveryTag { get; set; }
        public int ShardIndex { get; set; }
        public IShardProcessingContext? ShardContext { get; set; }

        public async Task AckAsync()
        {
            if (ShardContext == null) return;
            try
            {
                await ShardContext.AckAsync(DeliveryTag);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to ACK delivery tag {Tag} on Shard {Idx}", DeliveryTag, ShardIndex);
            }
        }

        public void Reset()
        {
            DeliveryTag = 0;
            ShardIndex = 0;
            ShardContext = null;
        }
    }
}