using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.ObjectPool;

namespace Damper.Infrastructure.MessageTransport
{
    public class MessageEnvelope
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string DestinationUrl { get; set; } = string.Empty;
        public ReadOnlyMemory<byte> RawPayloadBytes { get; set; }
        public Dictionary<string, string> Headers { get; set; } = [];
        public DateTime ReceivedAt { get; set; }
        public int AttemptCount { get; set; } = 1;
       
        // Uee a Webhook Ack Context that will come from an object pool to
        // eliminate the use of an anonymous "OnProcessingCompleteAsync" lambda
        public MessageAckContext? AckContext { get; set; }

        public static MessageEnvelope BuildBase(RequestWrapper rw)
        {
            var toReturn = new MessageEnvelope
            {
              CorrelationId = rw.CorrelationId,
              CustomerId = rw.CustomerId,
              ReceivedAt = DateTime.UtcNow,
              AttemptCount = 1,  
            };
            return toReturn;
        }

        public MessageEnvelope SetDestination(string toSet)
        {
            DestinationUrl = toSet;
            return this;
        }

        public MessageEnvelope SetPayload(ReadOnlyMemory<byte> toSet)
        {
            RawPayloadBytes = toSet;
            return this;
        }

        public MessageEnvelope SetHeaders(Dictionary<string, string> toSet)
        {
            Headers = toSet;
            return this;
        }
    }

    public static class WebhookEnvelopeExtensions
    {
        public static async Task FinalizeAckAsync(this MessageEnvelope envelope, ObjectPool<MessageAckContext> contextPool)
        {
            if (envelope.AckContext != null)
            {
                await envelope.AckContext.AckAsync();
                contextPool.Return(envelope.AckContext);
                envelope.AckContext = null;
            }
        }

        public static async Task FinalizeRejectAsync(this MessageEnvelope envelope, ObjectPool<MessageAckContext> contextPool)
        {
            if (envelope.AckContext != null)
            {
                await envelope.AckContext.RejectAsync(requeue: false);
                contextPool.Return(envelope.AckContext);
                envelope.AckContext = null;
            }
        }

        public static async Task FinalizeParkAsync(this MessageEnvelope envelope, ObjectPool<MessageAckContext> contextPool)
        {
            if (envelope.AckContext != null)
            {
                await envelope.AckContext.ParkForRetryAsync(envelope);
                contextPool.Return(envelope.AckContext);
                envelope.AckContext = null;
            }
        }

        public static bool HasAttemptsRemaining(this MessageEnvelope envelope, int maxAttempts)
        {
            return envelope.AttemptCount <= maxAttempts;
        }

        public static HttpRequestMessage BuildHttpRequest(this MessageEnvelope envelope, CustomerConfig custConfig)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, custConfig.DestinationURL)
            {
                Content = new ReadOnlyMemoryContent(envelope.RawPayloadBytes)
            };
            return request;
        }
    }
}