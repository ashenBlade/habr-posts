using BenchmarkDotNet.Attributes;
using PcMonitor.ValueTaskSource;

namespace PcMonitor.Benchmarks;

[MemoryDiagnoser]
public class ValueTaskBenchmarks
{
    private ValueTaskSourcePcMonitor _monitor;

    [GlobalSetup]
    public void Setup()
    {
        _monitor = new ValueTaskSourcePcMonitor(TimeSpan.FromMilliseconds(50));
        _monitor.Start();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _monitor.Stop();
        _monitor.Dispose();
        _monitor = null!;
    }

    [Benchmark]
    public async ValueTask MeasureValueTask()
    {
        await _monitor.GetStatisticsAsync();
    }

    [Benchmark]
    public async Task MeasureTask()
    {
        await _monitor.GetStatisticsTaskAsync();
    }
}