using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Damper.Infrastructure.QueueManagement
{
    public class PublishWrapper
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public CancellationToken CancelToken { get; set; }
        public bool ShouldThrow { get; set; }

        public static PublishWrapper BuildFrom(string correlationId, string customerId, string payload, CancellationToken ct, bool shouldThrow = false)
        {
            return new PublishWrapper
            {
                CorrelationId = correlationId,
                CustomerId = customerId,
                Payload = payload,
                CancelToken = ct,
                ShouldThrow = shouldThrow,
            };
        }

        public static PublishWrapper BuildBase(CancellationToken ct, bool shouldThrow = false)
        {
            return new PublishWrapper
            {
                CancelToken = ct,
                ShouldThrow = shouldThrow,
            };
        }

        public PublishWrapper SetCorrelationID(string toSet)
        {
            CorrelationId = toSet;
            return this;
        }

        public PublishWrapper SetCustomerID(string toSet)
        {
            CustomerId = toSet;
            return this;
        }

        public PublishWrapper SetPayload(string toSet)
        {
            Payload = toSet;
            return this;
        }

        public bool IsValid(out string invalidMessage)
        {
            invalidMessage = string.Empty;
            var sb = new StringBuilder();
            bool result = false;
            if (string.IsNullOrWhiteSpace(CorrelationId))
            {
                sb.Append($"Correlation ID can not be null or empty");
            }
            else if (string.IsNullOrWhiteSpace(CustomerId))
            {
                sb.Append($"{GetSeparator(sb)}Customer ID can not be null or empty");
            }
            else if (string.IsNullOrWhiteSpace(Payload))
            {
                sb.Append($"{GetSeparator(sb)}Payload can not be null or empty");
            }
            else
            {
                result = true;
            }
            invalidMessage = sb.ToString();
            return result;
        }

        private string GetSeparator(StringBuilder sb)
        {
            return sb.Length > 0 ? " | " : "";
        }

        public override string ToString()
        {
            return $"{nameof(CorrelationId)}: {CorrelationId} | {nameof(CustomerId)}: {CustomerId} | {nameof(Payload)}: {Payload} | {nameof(ShouldThrow)}: {ShouldThrow}";
        }
    }
}