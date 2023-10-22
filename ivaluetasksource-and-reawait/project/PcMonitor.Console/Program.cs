using System.Diagnostics;
using PcMonitor.ManualResetValueTaskSource;

using var monitor = new ValueTaskSourcePcMonitor(TimeSpan.FromMilliseconds(100));
monitor.Start();

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    try
    {
        cts.Cancel();
    }
    catch (Exception)
    {
        eventArgs.Cancel = false;
    }
};

await Task.Delay(250);
var watch = new Stopwatch();
while (!cts.Token.IsCancellationRequested)
{
    watch.Start();
    var statistics = await monitor.GetStatisticsAsync(cts.Token);
    var elapsed = watch.Elapsed;
    
    Console.WriteLine($"Температура CPU: {statistics.CpuTemperature}");
    Console.WriteLine($"Вызов занял: {elapsed}");
    Console.WriteLine();
    
    watch.Reset();
}