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
            // Signal the writer that no more items are coming
            try { Writer.TryComplete(); } catch { }

            // Cancel the background loop
            _cts.Cancel();
            _cts.Dispose();

            // Optional: To REALLYo ensure it's gone, await BackgroundTask.
            // But since Dispose() must be synchronous, we let it terminate in the background.
        }
    }
}