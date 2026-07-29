using System.ComponentModel.DataAnnotations;

namespace Damper.Infrastructure.ReferenceData
{
    public class ProcessorSettings
    {
        [Range(0, int.MaxValue)] public int WaitToWriteExpirationMillis { get; set; }
    }
}