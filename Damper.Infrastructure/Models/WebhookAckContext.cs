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

        public async Task AckAsync() => await ExecuteSafeAsync(() => ShardContext!.AckAsync(DeliveryTag));

        public async Task RejectAsync(bool requeue = false) => await ExecuteSafeAsync(async () => 
        {
            // Add this logging to verify the tag is valid (it must be > 0)
            _log.Info("<<< Attempting to Reject DeliveryTag: {Tag} on Shard: {Idx} >>>", DeliveryTag, ShardIndex);
            
            if (DeliveryTag == 0) throw new InvalidOperationException("Cannot Reject: DeliveryTag is 0.");
                
            await ShardContext!.RejectAsync(DeliveryTag, requeue);
        });

        private async Task ExecuteSafeAsync(Func<Task> action)
        {
            if (ShardContext == null) return;
            try { await action(); }
            catch (Exception ex)
            {
                _log.Error(ex, "<<< Failed to ACK/NACK delivery tag {Tag} on Shard {Idx} >>>", DeliveryTag, ShardIndex);
                // LOG THE FULL EXCEPTION DETAILS
                _log.Error(ex, "<<< CRITICAL: RabbitMQ Protocol Error on Shard {Idx}. Tag: {Tag} >>>", ShardIndex, DeliveryTag);
                throw; // DO NOT SWALLOW THIS EXCEPTION
            }
        }

        public async Task ParkForRetryAsync(WebhookEnvelope envelope) => await ExecuteSafeAsync(() =>
            ShardContext!.ParkForRetryAsync(envelope, DeliveryTag));
    }
}