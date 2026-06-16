using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Damper.Core.Verifiers
{
    public interface ISignatureVerifier
    {
        bool VerifyHmacSignature(string payload, string incomingSignature, string secretKey);
    }
}