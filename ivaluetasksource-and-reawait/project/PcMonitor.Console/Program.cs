using PcMonitor.ValueTaskSource;

using var monitor = new ValueTaskSourcePcMonitor(TimeSpan.FromMilliseconds(500));
monitor.Start();

using var cts = new CancellationTokenSource();

Console.CancelKeyPress += (sender, eventArgs) =>
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

while (!cts.Token.IsCancellationRequested)
{
    var statistics = await monitor.GetStatisticsAsync(cts.Token);
    Console.WriteLine($"Температура CPU: {statistics.CpuTemperature}");
    await Task.Delay(Random.Shared.Next(0, 666));
}
