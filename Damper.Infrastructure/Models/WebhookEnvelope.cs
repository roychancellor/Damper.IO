using System.Net.Http.Headers;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.ObjectPool;

namespace Damper.Infrastructure.Models
{
    public class WebhookEnvelope
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
        public WebhookAckContext? AckContext { get; set; }

        public static WebhookEnvelope BuildBase(RequestWrapper rw)
        {
            var toReturn = new WebhookEnvelope
            {
              CorrelationId = rw.CorrelationId,
              CustomerId = rw.CustomerId,
              ReceivedAt = DateTime.UtcNow,
              AttemptCount = 1,  
            };
            return toReturn;
        }

        public WebhookEnvelope SetDestination(string toSet)
        {
            DestinationUrl = toSet;
            return this;
        }

        public WebhookEnvelope SetPayload(ReadOnlyMemory<byte> toSet)
        {
            RawPayloadBytes = toSet;
            return this;
        }

        public WebhookEnvelope SetHeaders(Dictionary<string, string> toSet)
        {
            Headers = toSet;
            return this;
        }
    }

    public static class WebhookEnvelopeExtensions
    {
        public static async Task FinalizeAckAsync(this WebhookEnvelope envelope, ObjectPool<WebhookAckContext> contextPool)
        {
            if (envelope.AckContext != null)
            {
                await envelope.AckContext.AckAsync();
                contextPool.Return(envelope.AckContext);
                envelope.AckContext = null;
            }
        }

        public static async Task FinalizeRejectAsync(this WebhookEnvelope envelope, ObjectPool<WebhookAckContext> contextPool)
        {
            if (envelope.AckContext != null)
            {
                await envelope.AckContext.RejectAsync(requeue: false);
                contextPool.Return(envelope.AckContext);
                envelope.AckContext = null;
            }
        }

        public static async Task FinalizeParkAsync(this WebhookEnvelope envelope, ObjectPool<WebhookAckContext> contextPool)
        {
            if (envelope.AckContext != null)
            {
                await envelope.AckContext.ParkForRetryAsync(envelope);
                contextPool.Return(envelope.AckContext);
                envelope.AckContext = null;
            }
        }

        public static bool HasAttemptsRemaining(this WebhookEnvelope envelope, int maxAttempts)
        {
            return envelope.AttemptCount <= maxAttempts;
        }

        public static HttpRequestMessage BuildHttpRequest(this WebhookEnvelope envelope, CustomerConfig custConfig)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, custConfig.DestinationURL)
            {
                Content = new ReadOnlyMemoryContent(envelope.RawPayloadBytes)
            };
            return request;
        }
    }
}