using System.Threading;
using System.Threading.Tasks;
using AgenticTerminal.Startup;
using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;

namespace AgenticTerminal.Benchmarks;
[CPUUsageDiagnoser]
public class InteractiveStartupCoordinatorBenchmark
{
    [Params(10)]
    public int InitializationDelayMilliseconds { get; set; }

    [Benchmark]
    public Task RunAsync_WithDelayedInitialization()
    {
        return InteractiveStartupCoordinator.RunAsync(async cancellationToken => await Task.Delay(InitializationDelayMilliseconds, cancellationToken), _ => Task.CompletedTask, CancellationToken.None);
    }
}