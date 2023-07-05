using System.Diagnostics;
using System.Security.Principal;
using System.Threading.Tasks.Sources;
using PcMonitor.Core;

namespace PcMonitor.ValueTaskSource;

public class ManualPcStatisticsValueTaskSource: IValueTaskSource<PcStatistics>, IDisposable
{
    private static readonly Action<object?> Sentinel = _ =>
    {
        Debug.Assert(false, "Sentinel не должен был быть вызван. Работа завершена до вызова OnCompleted");
    };

    private ValueTaskSourcePcMonitor? _monitor;
    private TimeSpan _lastMeasurementTime = TimeSpan.Zero;
    private CancellationToken _cancellationToken;
    
    private PcStatistics _cachedResult = new();
    private Exception? _exception;

    private object? _state;
    private object? _scheduler;
    private Action<object?>? _continuation;
    private short _version;
    private ExecutionContext? _ec;
    
    

    private readonly Timer _timer;
    private static void OnTimerTimeout(object? state)
    {
        var source = ( ManualPcStatisticsValueTaskSource ) state!;
        try
        {
            source._cancellationToken.ThrowIfCancellationRequested();
            
            Debug.Assert(source._monitor is not null, "source._monitor is not null");
            var (statistics, lastTime) = source._monitor.LastMeasurement;
            source._lastMeasurementTime = lastTime;
            source._cachedResult = statistics;
            source.NotifyCompleted();
        }
        catch (Exception e)
        {
            source._exception = e;
            source.NotifyCompleted();
        }
    }

    private void NotifyCompleted()
    {
        // Опять состояние гонки
        var previous = Interlocked.CompareExchange(ref _continuation, Sentinel, null);
        if (previous is null)
        {
            return;
        }

        if (_ec is {} ec)
        {
            _ec = null;
            ExecutionContext.Run(ec, state =>
            {
                var tuple = ( Tuple<ManualPcStatisticsValueTaskSource, Action<object?>, object?> ) state!;
                tuple.Item1.InvokeContinuation(tuple.Item2, tuple.Item3, false);
            }, Tuple.Create(this, previous, _state));
        }
        else
        {
            InvokeContinuation(previous, _state, false);
        }
    }

    private void CheckVersion(short version)
    {
        if (version != _version)
        {
            throw new InvalidOperationException("Обнаружено множественное ожидание");
        }
    }
    
    public PcStatistics GetResult(short version)
    {
        CheckVersion(version);
        if (_exception is not null)
        {
            var exception = _exception!;
            Reset();
            throw exception;
        }

        if (_cancellationToken.IsCancellationRequested)
        {
            var token = _cancellationToken;
            Reset();
            token.ThrowIfCancellationRequested();
        }

        if (_cachedResult == default)
        {
            // Результат еще не готов
            throw new InvalidOperationException("Работа еще не завершена");
        }
        
        var result = _cachedResult;
        Reset();
        return result;

        void Reset()
        {
            _version++;
            _cachedResult = default;
            _exception = null;
            _state = null;
            _continuation = null;
            _cancellationToken = default;
        }
    }

    public ValueTaskSourceStatus GetStatus(short token)
    {
        CheckVersion(token);
        if (_cancellationToken.IsCancellationRequested)
        {
            return ValueTaskSourceStatus.Canceled;
        }
        
        if (_exception is not null)
        {
            return ValueTaskSourceStatus.Faulted;
        }

        if (_cachedResult != default)
        {
            // Предположим, что настоящий результат не должен быть default
            return ValueTaskSourceStatus.Succeeded;
        }

        return ValueTaskSourceStatus.Pending;
    }

    private void InvokeContinuation(Action<object?>? continuation, object? state, bool synchronously)
    {
        if (continuation is null)
        {
            return;
        }

        if (_scheduler is not null)
        {
            if (_scheduler is SynchronizationContext sc)
            {
                sc.Post(s =>
                {
                    var t = ( Tuple<Action<object?>, object?> ) s!;
                    t.Item1(t.Item2);
                }, Tuple.Create(continuation, state));
            }
            else
            {
                var ts = ( TaskScheduler ) _scheduler;
                Task.Factory.StartNew(continuation, 
                    state, CancellationToken.None,
                    TaskCreationOptions.DenyChildAttach, ts);
            }
        }
        else if (synchronously)
        {
            continuation(state);
        }
        else
        {
            ThreadPool.QueueUserWorkItem(continuation, state, true);
        }
    }
    
    public void OnCompleted(Action<object?> continuation,
                            object? state,
                            short token,
                            ValueTaskSourceOnCompletedFlags flags)
    {
        CheckVersion(token);
        
        if (UseExecutionContext())
        {
            _ec = ExecutionContext.Capture();
        }

        if (UseSchedulingContext() && 
            GetScheduler() is {} scheduler)
        {
            _scheduler = scheduler;
        }

        // Здесь может быть состояние гонки, когда 
        // результат выставляется быстрее, чем заканчивается вызов OnCompleted.
        // В нашем случае, такое может случиться, когда время ожидания таймера было очень мало
        _state = state;
        var prev = Interlocked.CompareExchange(ref _continuation, continuation, null);
        if (prev is null)
        {
            return;
        }
        
        _state = null;
        if (!ReferenceEquals(prev, Sentinel))
        {
            throw new InvalidOperationException("Обнаружено множественное ожидание");
        }
        
        // Вызываем продолжение синхронно, т.к. уже результат уже есть
        InvokeContinuation(continuation, state, true);
        
        bool UseExecutionContext() => ( flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext ) is not ValueTaskSourceOnCompletedFlags.None;

        bool UseSchedulingContext() =>
            ( flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext ) is not ValueTaskSourceOnCompletedFlags
               .None;

        object GetScheduler() => ( object? ) SynchronizationContext.Current ?? TaskScheduler.Current;
    }

    public ValueTask<PcStatistics> Start(ValueTaskSourcePcMonitor monitor, CancellationToken token = default)
    {
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
        return new ValueTask<PcStatistics>(this, _version);
    }

    public ManualPcStatisticsValueTaskSource()
    {
        _timer = new Timer(OnTimerTimeout, this, Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        _monitor?.Dispose();
        _timer.Dispose();
    }
}