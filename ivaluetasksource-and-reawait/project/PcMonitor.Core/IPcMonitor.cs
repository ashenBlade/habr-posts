namespace PcMonitor.Core;

public interface IPcMonitor
{
    public ValueTask<PcStatistics> GetStatisticsAsync(CancellationToken token = default);
}