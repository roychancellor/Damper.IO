using System.Text;
using Damper.Domain.Common;
using Damper.Domain.Integrations;
using Microsoft.Extensions.ObjectPool;

namespace Damper.Infrastructure.MessageTransport
{
    public class MessageEnvelope
    {
        public CorrelationId CorrelationId { get; set; } = new(string.Empty);
        public ApiKey ApiKey { get; set; }
        public long IntegrationId { get; set; }
        public IntegrationName IntegrationName { get; set; } = new(string.Empty);
        public string DestinationUrl { get; set; } = string.Empty;
        public ReadOnlyMemory<byte> RawPayloadBytes { get; set; }
        public Dictionary<string, string> Headers { get; set; } = [];
        public DateTime ReceivedAt { get; set; }
        public int AttemptCount { get; set; } = 1;
        public bool ShouldThrow { get; set; }
        public CancellationToken CancelToken { get; set; }
       
        // Uee a Message Ack Context that will come from an object pool to
        // eliminate the use of an anonymous "OnProcessingCompleteAsync" lambda
        public MessageAckContext? AckContext { get; set; }

        public static MessageEnvelope BuildBase(RequestWrapper rw, CancellationToken token, bool shouldThrow)
        {
            var toReturn = new MessageEnvelope
            {
              CancelToken = token,
              ShouldThrow = shouldThrow,
              ApiKey = rw.ApiKey,
              CorrelationId = rw.CorrelationId,
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

        public MessageEnvelope SetIntegrationId(long toSet)
        {
            IntegrationId = toSet;
            return this;
        }

        public MessageEnvelope SetIntegrationName(string toSet)
        {
            IntegrationName = new(toSet);
            return this;
        }

        public bool IsValid(out string invalidMessage)
        {
            invalidMessage = string.Empty;
            var sb = new StringBuilder();
            bool result = false;
            if (string.IsNullOrWhiteSpace(CorrelationId.Value))
            {
                sb.Append($"Correlation ID can not be null or empty");
            }
            else if (IntegrationId <= 0)
            {
                sb.Append($"{GetSeparator(sb)}Integration ID must be a positive long");
            }
            else if (RawPayloadBytes.IsEmpty)
            {
                sb.Append($"{GetSeparator(sb)}Payload can not be empty");
            }
            else
            {
                result = true;
            }
            invalidMessage = sb.ToString();
            return result;
        }

        private static string GetSeparator(StringBuilder sb)
        {
            return sb.Length > 0 ? " | " : "";
        }

        public override string ToString()
        {
            return $"{nameof(CorrelationId)}: {CorrelationId} | {nameof(IntegrationId)}: {IntegrationId} | {nameof(RawPayloadBytes)}: REDACTED | {nameof(ShouldThrow)}: {ShouldThrow}";
        }
    }

    public static class MessageEnvelopeExtensions
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

        public static HttpRequestMessage BuildHttpRequest(this MessageEnvelope envelope, Integration integration)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, integration.Delivery.Destination.Uri)
            {
                Content = new ReadOnlyMemoryContent(envelope.RawPayloadBytes)
            };
            return request;
        }
    }
}