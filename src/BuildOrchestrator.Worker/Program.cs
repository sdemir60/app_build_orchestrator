using BuildOrchestrator.Worker;
using BuildOrchestrator.Worker.MsBuild;

// Section 2: MSBuild must be located before any Microsoft.Build type is touched.
MsBuildInitializer.EnsureRegistered();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

var channel = MessageChannel.Stdio();
using var host = new WorkerHost(channel);
await host.RunAsync(cts.Token).ConfigureAwait(false);
