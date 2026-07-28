using System.Threading.Channels;
using Damper.Infrastructure.Models;

namespace Damper.Infrastructure.CustomerChannels
{
    public class CustomerEgressPipeline : IDisposable
    {
        public ChannelWriter<WebhookEnvelope> Writer { get; }
        public Task BackgroundTask { get; }
        private readonly CancellationTokenSource _cts;

        public CustomerEgressPipeline(ChannelWriter<WebhookEnvelope> writer, Task backgroundTask, CancellationTokenSource cts)
        {
            Writer = writer;
            BackgroundTask = backgroundTask;
            _cts = cts;
        }

        public void Dispose()
        {
            // 1. Signal the writer that no more items are coming
            try { Writer.TryComplete(); } catch { }

            // 2. Cancel the background loop
            _cts.Cancel();
            _cts.Dispose();

            // 3. Optional: If you need to ensure it's gone, you could await BackgroundTask.
            // But since Dispose() must be synchronous, we let it terminate in the background.
        }
    }
}