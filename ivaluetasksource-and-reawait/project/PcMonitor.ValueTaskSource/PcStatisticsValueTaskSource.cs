using System.Diagnostics;
using System.Threading.Tasks.Sources;
using Microsoft.Extensions.ObjectPool;
using PcMonitor.Core;

namespace PcMonitor.ValueTaskSource;

internal class PcStatisticsValueTaskSource: IValueTaskSource<PcStatistics>, IDisposable
{
    private ObjectPool<PcStatisticsValueTaskSource>? _pool;
    private ValueTaskSourcePcMonitor? _monitor;
    private TimeSpan _lastMeasurementTime = TimeSpan.Zero;
    private PcStatistics _cachedResult = new();

    private ManualResetValueTaskSourceCore<PcStatistics> _source = new();

    private CancellationToken _cancellationToken;
    private readonly Timer _timer;

    private static void OnTimerTimeout(object? state)
    {
        var source = ( PcStatisticsValueTaskSource ) state!;
        try
        {
            source._cancellationToken.ThrowIfCancellationRequested();
            
            Debug.Assert(source._monitor is not null, "source._monitor is not null");
            var (statistics, lastTime) = source._monitor.LastMeasurement;
            source._lastMeasurementTime = lastTime;
            source._cachedResult = statistics;
            source._source.SetResult(statistics);
        }
        catch (Exception e)
        {
            source._source.SetException(e);
        }
    }

    public PcStatistics GetResult(short token)
    {
        try
        {
            return _source.GetResult(token);
        }
        finally
        {
            Debug.Assert(_pool is not null, "pool is not null");
            _pool.Return(this);
            _pool = null;
            _monitor = null;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _source.Reset();
        }
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        return _source.GetStatus(token);
    }

    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
    {
        _source.OnCompleted(continuation, state, token, flags);
    }

    public ValueTask<PcStatistics> Start(ValueTaskSourcePcMonitor monitor, ObjectPool<PcStatisticsValueTaskSource> pool, CancellationToken token = default)
    {
        _pool = pool;
        _monitor = monitor;
        _cancellationToken = token;
        
        // Если замер достаточно свежий, то возвращаем его
        if (_monitor.IsMeasurementActual(_lastMeasurementTime))
        {
            return new ValueTask<PcStatistics>(_cachedResult);
        }

        // Иначе засыпаем до момента,
        // когда новое значение будет прочитано
        var sleepTime = _monitor.GetTimeBeforeNextScrap();
        var success = _timer.Change(sleepTime, Timeout.InfiniteTimeSpan);
        if (!success)
        {
            throw new Exception("Не удалось запустить таймер");
        }
        return new ValueTask<PcStatistics>(this, _source.Version);
    }

    public PcStatisticsValueTaskSource()
    {
        _timer = new Timer(OnTimerTimeout, this, Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        _monitor?.Dispose();
        _timer.Dispose();
    }
}