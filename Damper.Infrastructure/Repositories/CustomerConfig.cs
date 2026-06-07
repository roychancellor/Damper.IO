using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Damper.Infrastructure.Repositories
{
    public class CustomerConfig
    {
        public string CustomerId { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public string WebhookHeaderKey { get; set; } = "";
    }
}