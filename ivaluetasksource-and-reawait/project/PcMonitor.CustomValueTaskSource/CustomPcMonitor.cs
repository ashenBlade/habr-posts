using System.Diagnostics;
using PcMonitor.Core;

namespace PcMonitor.CustomValueTaskSource;

public class CustomPcMonitor: IDisposable, IPcMonitor
{
    private readonly TimeSpan _scrapTimeout;
    private PcStatistics _cache;
    private TimeSpan _prevScrapTime = TimeSpan.Zero;
    private TimeSpan _nextScrapTime = TimeSpan.Zero;
    private readonly Timer _updateTimer;
    private bool _started;

    internal (PcStatistics Measurement, TimeSpan ScrapTime) LastMeasurement => ( _cache, _prevScrapTime );

    public CustomPcMonitor(TimeSpan scrapTimeout)
    {
        _scrapTimeout = scrapTimeout;
        _updateTimer = new Timer(OnTimeout, this, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        _started = true;
        if (!_updateTimer.Change(TimeSpan.Zero, _scrapTimeout))
        {
            throw new Exception("Не удалось запустить таймер");
        }
    }
    
    public void Stop()
    {
        _started = false;
        if (!_updateTimer.Change(Timeout.Infinite, Timeout.Infinite))
        {
            throw new Exception("Не удалось остановить таймер");
        }
    }

    private static readonly TimeSpan Delta = TimeSpan.FromMilliseconds(10);
    internal bool IsMeasurementActual(TimeSpan savedCacheTimestampTicks)
    {
        if (!_started)
        {
            throw new Exception("Еще не запущен");
        }
        
        return _prevScrapTime - savedCacheTimestampTicks <= Delta;
    }

    private static void OnTimeout(object? state)
    {
        var monitor = ( CustomPcMonitor ) state!;
        var now = GetNow();
        try
        {
            monitor._cache = new PcStatistics(Random.Shared.Next(10, 60));
            monitor._prevScrapTime = now;
        }
        finally
        {
            monitor._nextScrapTime = now + monitor._scrapTimeout;
        }
    }

    public TimeSpan GetTimeBeforeNextScrap()
    {
        if (!_started)
        {
            throw new Exception("Еще не запущен");
        }

        // Еще не собрал статистику - только запускается.
        // Поэтому подождем пока запустится и отработает 1 цикл
        if (_prevScrapTime == TimeSpan.Zero)
        {
            return _scrapTimeout;
        }

        // Иначе данные уже были собраны 
        // Тогда высчитываем время сна до следующего сбора
        var now = GetNow();
        var delta = _nextScrapTime - now;
        // Такое может случиться из-за состояния гонки
        return TimeSpan.Zero < delta
                   ? delta
                   : _scrapTimeout;
    }

    internal static TimeSpan GetNow() => TimeSpan.FromTicks(Stopwatch.GetTimestamp());

    public ValueTask<PcStatistics> GetStatisticsAsync(CancellationToken token = default)
    {
        var x = new ManualValueTaskSource();
        return x.Start(this);
    }

    public async Task<PcStatistics> GetStatisticsTaskAsync(CancellationToken token = default)
    {
        await Task.Delay(GetTimeBeforeNextScrap(), token);
        return _cache;
    }

    public void Dispose()
    {
        Stop();
        _started = false;
        _updateTimer.Dispose();
    }
}