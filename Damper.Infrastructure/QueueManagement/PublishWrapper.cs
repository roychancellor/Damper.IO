using System;
using System.Collections.Generic;
using System.Linq;
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
    }
}