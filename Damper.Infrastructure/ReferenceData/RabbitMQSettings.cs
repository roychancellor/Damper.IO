using System.ComponentModel.DataAnnotations;

namespace Damper.Infrastructure.ReferenceData
{
    public class RabbitMQSettings
    {
        [Required] public string HostName { get; set; } = string.Empty;
        [Required] public string UserName { get; set; } = string.Empty;
        [Required] public string Password { get; set; } = string.Empty;
        [Required] public string ExchangeName { get; set; } = string.Empty;
        [Required] public string VirtualHost { get; set; } = string.Empty;
        [Range(0, int.MaxValue)] public int Port { get; set; }
        [Range(1, int.MaxValue)] public int NumberOfShards { get; set; }
        [Required] public string IngressShardPrefix { get; set; } = string.Empty;
        [Required] public string DeadLetterExchange { get; set; } = string.Empty;
        [Required] public string DeadLetterQueue { get; set; } = string.Empty;
        [Range(0, uint.MaxValue)] public uint PrefetchSize { get; set; }
        [Range(1, ushort.MaxValue)] public ushort PrefetchCount { get; set; }
        [Required] public bool IsPrefetchGlobal { get; set; }
        [Required] public string ParkingLotExchange { get; set; } = string.Empty;
        [Range(0, int.MaxValue)] public int ParkingLotBaseTTLMillis { get; set; }
        [Range(0, int.MaxValue)] public int ParkingLotJitterMillis { get; set; }
        [Range(1, int.MaxValue)] public int DefaultMaxQueueCapacity { get; set; }
    }
}