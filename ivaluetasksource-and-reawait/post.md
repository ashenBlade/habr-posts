Содержание:
1. ValueTask до и после .NET Core 2.1 - добавление IValueTaskSource
2. Устройство и алгоритм работы IValueTaskSource
3. Делаем полностью своими руками
4. Добавляем ManualResetValueTaskSource
5. Как реализован сокет с помощью него (все делают пример на нем, я не исключение)
6. Бенчмарки?
7. Ссылки

# ValueTask до и после .NET Core 2.1

Обычно `ValueTask` используют ради оптимизации. 
Например, возврат закэшированного результата, или `ValueTask.FromCancelled` с переданным `CancellationToken`.

Но нет предела оптимизациям и одним ранним выходом теперь не обойтись.
Поэтому был добавлен `IValueTaskSource`.

Теперь `ValueTask` можно создать не только передав готовый результат или `Task`, но 
и упомянутый выше `IValueTaskSource`.

```cs
// Конструкторы
public ValueTask(IValueTaskSource source, short token);
public ValueTask(Task task);
public ValueTask<T>(T result);
```

Что это такое мы с вами сейчас и узнаем

# Устройство и алгоритм работы IValueTaskSource

Интерфейс `IValueTaskSource` - набор из 3 методов:

```cs
public interface IValueTaskSource<out TResult>
{
    // Получить статус выполнения текущей операции  
    ValueTaskSourceStatus GetStatus(short token);

    // Запланировать продолжение на выполнение при завершении работы
    void OnCompleted(
      Action<object?> continuation,
      object? state,
      short token,
      ValueTaskSourceOnCompletedFlags flags);

    // Получить готовый результат операции
    TResult GetResult(short token);
}
```

`GetStatus` - получает статус выполнения.
Статус представляется перечислением `ValueTaskSourceStatus`

```cs
public enum ValueTaskSourceStatus
{
    // В процессе
    Pending,
    // Успешно завершилась
    Succeeded,
    // Завершилась ошибкой (исключением)
    Faulted,
    // Отменена
    Canceled,
}
```

Этот метод вызывается 1 раз, после создания `ValueTask`.
Если операция уже завершилась, то вызывается `GetResult` для получения результата.
Если операция в процессе, то вызывается `OnCompleted` для регистирования продолжения на выполнение.

`OnCompleted` - регистрирует переданное продолжение на выполнение по окончании операции.
Вызывается после `GetStatus`. 

На вход ему подаются:
- `Action<object> continuation` - само продолжение
- `object state` - объект состояния, который передается `continuation`
- `ValueTaskSourceOnCompletedFlags flags` - специальные флаги, указывающие поведение при вызове продолжения

Флаги представляются перечислением `ValueTaskSourceOnCompletedFlags`:
```cs
[Flags]
public enum ValueTaskSourceOnCompletedFlags
{
    // Без указаний
    None = 0,
    // Необходимо использовать текущий SynchronizationContext для продолжения
    UseSchedulingContext = 1,
    // В продолжении нужно использовать текущий ExecutionContext
    FlowExecutionContext = 2,
}
```

Между вызовом `GetStatus` и `OnCompleted` может пройти какое-то время и операция завершится. 
Поэтому во время выполнения `OnCompleted` работа может быть уже закончена. 
В таких случаях, продолжение обычно выполняется тут же.

`GetResult` - получает результат операции. 
Этот метод вызывается 1 раз при завершении работы для получения результата: возвращаемый объект или исключение.

> Он должен быть вызван тогда, когда операция только завершилась.
> Моей ошибкой во время первой реализации было то, что я использовать семафор для ожидания выполнения.
> Но из-за неправильных вызовов случился дедлок: 
>  - Фоновый поток завершил операцию и выставил результат
>  - В этот момент вызвалось продолжение 
>  - Продолжение зашло в `GetResult` и остановилось на семафоре
>  - Фоновый поток не получил обратно управление, т.к. продолжение было вызвано, но семафор еще не выставилен

Также во всех методах присутствует `token`. 
Это специальное значения для обнаружения множественных `await`.
Зачем они нужны поговорим далее.

Алгоритм работы может быть 2 вариантов и зависеть от ответа `GetStatus`.
- `Pending` - операция не завершилась, поэтому нужно запланировать дальнейшее выполнение:
   1. `GetStatus`
   2. `OnCompleted`
   3. `GetResult`
- В остальных случаях выполнение уже завершилось:
   1. `GetStatus`
   2. `GetResult`

Здесь можно провести аналогию с тем, как работает магия `async/await` и ее машины состояний.
Грубо говоря, мы создали свой собственный `Task` с блэкджеком, но без аллокаций.

# Делаем полностью своими руками

Теперь сделаем свою реализацию. 

Представим, перед нами задача получения статистики ПК.
У нас есть класс `PcMonitor`, отдающий эту статистику. 
Он вызывается очень часто, поэтому для оптимизации мы решили:
- Опрашивать ПК не на каждый вызов, а с определенным интервалом и хранить полученные значения в кэше
- Если при вызове значение из кэша еще актуально, то вернуть его, иначе ждать до следующего сбора

<spoiler title="Детали реализации">

Статистика представляется структурой `PcStatistics`. 
Пока там только температура процессора, но вы придумайте, что туда еще можно добавить.

```cs
public readonly record struct PcStatistics(double CpuTemperature);
```

В реализации используется класс `ValueTaskSourcePcMonitor`.
Сбор статистики реализован с помощью `System.Threading.Timers.Timer`, 
который с определенным интервалом кладет в кэш новое значение и обновляет время сбора.

Время сбора представляет `TimeSpan`, получаемый с помощью `Stopwatch` (не самая лучшая идея, но сойдет)

</spoiler>

Наша реализация `IValueTaskSource` представляется классом `PcStatisticsManualResetValueTaskSource`.
Он хранит в себе необходимые для работы данные.
```cs
// Результат работы
private CancellationToken _cancellationToken;
private PcStatistics _cachedResult = new();
private Exception? _exception;

// Инфраструтура для работы IValueTaskSource
private object? _state;
private object? _scheduler;
private Action<object?>? _continuation;
private short _version;
private ExecutionContext? _ec;

// Инфраструктура бизнес-логики
private readonly Timer _timer;
private TimeSpan _lastMeasurementTime = TimeSpan.Zero;
private CustomPcMonitor? _monitor;
```

`GetStatus`

Пожалуй, его реализация самая простая
```cs
public ValueTaskSourceStatus GetStatus(short token)
{
    CheckVersion(token);
    
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
```

Дальше `OnCompleted`

```cs
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
    
    // Sentinel - маркер, указывающий на конкурентное выполнение
    if (!ReferenceEquals(prev, Sentinel))
    {
        throw new InvalidOperationException("Обнаружено множественное ожидание");
    }
    
    // Вызываем продолжение синхронно, т.к. уже результат уже готов
    InvokeContinuation(continuation, state, synchronously: true);
    
    bool UseExecutionContext() => ( flags & ValueTaskSourceOnCompletedFlags.FlowExecutionContext ) is not ValueTaskSourceOnCompletedFlags.None;

    bool UseSchedulingContext() =>
        ( flags & ValueTaskSourceOnCompletedFlags.UseSchedulingContext ) is not ValueTaskSourceOnCompletedFlags.None;

    object GetScheduler() => ( object? ) SynchronizationContext.Current ?? TaskScheduler.Current;
}
```

Теперь доходим до реализации `GetResult`

```cs
public PcStatistics GetResult(short version)
{
    CheckVersion(version);
    
    if (_exception is not null)
    {
        throw _exception;
    }

    if (_cachedResult == default)
    {
        // Результат еще не готов
        throw new InvalidOperationException("Работа еще не завершена");
    }

    return _cachedResult;
}
```

Все приведенные выше методы довольно просты в реализации, но на прод их не принесешь:
- Нет поддержки отмены
- Плохая работа с конкурентностью
- `ExecutionContext` не используется

<spoiler title="Реализация InvokeContinuation">

Вот реализация вызова продолжения

```cs
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
```

</spoiler>

# Добавляем ManualResetValueTaskSource

Реализацию написали. Мы молодцы. А теперь все выбрасываем, так как реализация за нас уже сделана - `ManualResetValueTaskSourceCore`.
Она реализует все выше приведенные методы логики `IValueTaskSource`.
Теперь перепишем старые методы с его использованием.

```cs
public PcStatistics GetResult(short token)
{
    return _source.GetResult(token); 
}

public ValueTaskSourceStatus GetStatus(short token)
{
    return _source.GetStatus(token);
}

public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
{
    _source.OnCompleted(continuation, state, token, flags);
}
```

Благодаря этому, можно писать свои `ValueTaskSource` и задумываться, только о бизнес-логике

# Добавляем пулинг + почему нельзя заново await'ить

Теперь время для оптимизаций.
Заметим, что `GetResult` вызывается только 1 раз. 
Почему бы не переиспользовать создаваемые `ValueTaskSource` и возвращать их обратно при вызове `GetResult`.

Так и сделаем. 
В `PcMonitor` добавим пул этих объектов: при вызове `GetStatisticsAsync` берем из, а в `GetResult` будем возвращать обратно в.

```cs
// PcStatisticsManualResetValueTaskSource
private ObjectPool<PcStatisticsManualResetValueTaskSource>? _pool;

public PcStatistics GetResult(short token)
{
    try
    {
        return _source.GetResult(token);
    }
    finally
    {
        _pool.Return(this);
        // Сбрасываем состояние, чтобы использовать дальше
        _source.Reset();   
    }
}

// PcMonitor
private ObjectPool<PcStatisticsManualResetValueTaskSource> _pool;

public ValueTask<PcStatistics> GetStatisticsAsync(CancellationToken token = default)
{
    var source = _pool.Get();
    return source.Start(this, _pool, token);
}
```

Теперь мы можем создать ограниченное количество `ValueTaskSource` и постоянно их переиспользовать без лишних аллокаций памяти!

Но что, если кто-то попытается за`await`'ить `ValueTask` несколько раз? 
Тогда _в лучшем случае_ ему вернется старый результат.

Но даже если эти вызовы были крайне близки во времени, никто не гарантирует, что за это время тот же самый `ValueTaskSource` не будет передан в другой `ValueTask` и будет хранить новое значение.

Вот тут и нужен `short token`, передававшийся в любой метод. 
Он призван проверять, что вызвавший код, обращается к `ValueTask`, в которой находится актуальный `ValueTaskSource`.
В `ManualResetValueTaskSourceCore` он реализован в виде простого счетчика, поэтому там он называется `Version`.
Но в общем случае это не обязательно - сойдет любое неповторяющееся значение.

Этот токен задается в самом начале и не изменяется в процессе работы `ValueTask`
```cs
public ValueTask<PcStatistics> Start(ValueTaskSourcePcMonitor monitor, ObjectPool<PcStatisticsManualResetValueTaskSource> pool, CancellationToken token = default)
{
    // ...
    
    // ManualResetValueTaskSourceCore.Version - токен, который инкрементируется при вызове Reset()
    return new ValueTask<PcStatistics>(this, _source.Version);
}
```

# Примеры реализации из .NET

Когда кто-то представляет `IValueTaskSource`, почти всегда в пример приводят сокет.
Я не буду исключением.

Читать или писать в сокет можно только одним потоком (только один или читает или пишет).
Растратно каждый раз создавать новые `Task`'и на каждый чих (особенно учитывая что ["Сеть надежна"](https://ru.wikipedia.org/wiki/Заблуждения_о_распределённых_вычислениях)).
Поэтому внутри себя сокет содержит 2 буфера `IValueTaskSource` - для чтения и записи
```cs
public partial class Socket
{
    /// <summary>Cached instance for receive operations that return <see cref="ValueTask{Int32}"/>. Also used for ConnectAsync operations.</summary>
    private AwaitableSocketAsyncEventArgs? _singleBufferReceiveEventArgs;
    /// <summary>Cached instance for send operations that return <see cref="ValueTask{Int32}"/>. Also used for AcceptAsync operations.</summary>
    private AwaitableSocketAsyncEventArgs? _singleBufferSendEventArgs;
    
    // ...
    
    internal sealed class AwaitableSocketAsyncEventArgs 
      : SocketAsyncEventArgs, 
        IValueTaskSource, 
        IValueTaskSource<int>, 
        IValueTaskSource<Socket>, 
        IValueTaskSource<SocketReceiveFromResult>, 
        IValueTaskSource<SocketReceiveMessageFromResult>
    {
        // ...
    }
}
```

Например, при чтении из сокета буфер используется таким образом:
```cs
internal ValueTask<int> ReceiveAsync(Memory<byte> buffer, SocketFlags socketFlags, bool fromNetworkStream, CancellationToken cancellationToken)
{
    // Получаем закэшированный IValueTaskSource или создаем новый (потом положим обратно в кэш)
    AwaitableSocketAsyncEventArgs saea =
        Interlocked.Exchange(ref _singleBufferReceiveEventArgs, null) ??
        new AwaitableSocketAsyncEventArgs(this, isReceiveForCaching: true);
    
    // Обновляем состояние IValueTaskSource для новой работы
    saea.SetBuffer(buffer);
    saea.SocketFlags = socketFlags;
    saea.WrapExceptionsForNetworkStream = fromNetworkStream;

    // Запускаем асинхронную операцию
    return saea.ReceiveAsync(this, cancellationToken);
}

internal sealed class AwaitableSocketAsyncEventArgs 
{
    public ValueTask<int> ReceiveAsync(Socket socket, CancellationToken cancellationToken)
    {
        if (socket.ReceiveAsync(this, cancellationToken))
        {
            // Операция не завершена синхронно - запускаем асинхронную операцию
            _cancellationToken = cancellationToken;
            return new ValueTask<int>(this, _token);
        }
    
        int bytesTransferred = BytesTransferred;
        SocketError error = SocketError;
    
        Release();
        
        // Операция завершилась синхронно
        return error == SocketError.Success ?
            new ValueTask<int>(bytesTransferred) :
            ValueTask.FromException<int>(CreateException(error));
    }
}
```

`IValueTaskSource` используется также в `Channel`'ах.
Он используется как в `Bounded` так и в `Unbounded`, но пример сделаю на `Bounded`.

В `BoundedChannel` есть следующие поля
```cs
internal sealed class BoundedChannel<T> : Channel<T>, IDebugEnumerable<T>
{
    /// <summary>Readers waiting to read from the channel.</summary>
    private readonly Deque<AsyncOperation<T>> _blockedReaders = new Deque<AsyncOperation<T>>();
    /// <summary>Writers waiting to write to the channel.</summary>
    private readonly Deque<VoidAsyncOperationWithData<T>> _blockedWriters = new Deque<VoidAsyncOperationWithData<T>>();
    /// <summary>Linked list of WaitToReadAsync waiters.</summary>
    private AsyncOperation<bool>? _waitingReadersTail;
    /// <summary>Linked list of WaitToWriteAsync waiters.</summary>
    private AsyncOperation<bool>? _waitingWritersTail;
    // ...
}
```

Как можно заметить, здесь есть поля, использующие `AsyncOperation` - тот самый `IValueTaskSource`:
```cs
internal partial class AsyncOperation<TResult> 
    : AsyncOperation, 
      IValueTaskSource, 
      IValueTaskSource<TResult>
{
    // Предназначен ли для пулинга
    private readonly bool _pooled;
    // Асинхронное продолжение
    private readonly bool _runContinuationsAsynchronously;
    // Результат операции
    private TResult? _result;
    // Исключение в процессе работы
    private ExceptionDispatchInfo? _error;
    // continuation из OnCompleted
    private Action<object?>? _continuation;
    // state из OnCompleted
    private object? _continuationState;
    // SynchronizationContext или TaskScheduler
    private object? _schedulingContext;
    private ExecutionContext? _executionContext;
    // token
    private short _currentId;
}
```

Для чтения из канала используется `ValueTask<T> ReadAsync`:
```cs
public override ValueTask<T> ReadAsync(CancellationToken cancellationToken)
{
    BoundedChannel<T> parent = _parent;
    lock (parent.SyncObj)
    {
        // Если есть свободные элементы - вернуть их
        if (!parent._items.IsEmpty)
        {
            return new ValueTask<T>(DequeueItemAndPostProcess());
        }
        
        // Используем закэшированный IValueTaskSource
        if (!cancellationToken.CanBeCanceled)
        {
            AsyncOperation<T> singleton = _readerSingleton;
            if (singleton.TryOwnAndReset())
            {
                parent._blockedReaders.EnqueueTail(singleton);
                return singleton.ValueTaskOfT;
            }
        }

        // Возвращаем новый IValueTaskSource
        var reader = new AsyncOperation<T>(parent._runContinuationsAsynchronously | cancellationToken.CanBeCanceled, cancellationToken);
        parent._blockedReaders.EnqueueTail(reader);
        return reader.ValueTaskOfT;
    }
}
```

Для записи - `ValueTask WriteAsync`:
```cs
public override ValueTask WriteAsync(T item, CancellationToken cancellationToken)
{
    // Количество элемент в очереди
    int count = parent._items.Count;

    if (count == 0)
    {
        // Добавляем элемент в свободную очередь или заблокированного читателя
    }
    else if (count < parent._bufferedCapacity)
    {
        // Синхронно добавляем элемент в свободную очередь
    }
    else if (parent._mode == BoundedChannelFullMode.Wait)
    {
        // Очередь полна, создаем асинхронную операцию записи

        // Используем закэшированный IValueTaskSource
        if (!cancellationToken.CanBeCanceled)
        {
            VoidAsyncOperationWithData<T> singleton = _writerSingleton;
            if (singleton.TryOwnAndReset())
            {
                singleton.Item = item;
                parent._blockedWriters.EnqueueTail(singleton);
                return singleton.ValueTask;
            }
        }

        // Создаем новый IValueTaskSource
        var writer = new VoidAsyncOperationWithData<T>(runContinuationsAsynchronously: true, cancellationToken);
        writer.Item = item;
        parent._blockedWriters.EnqueueTail(writer);
        return writer.ValueTask;
    }
    else if (parent._mode == BoundedChannelFullMode.DropWrite)
    {
        // Отбрасываем элемент, т.к. очередь полна
    }
    else
    {
        // Удаляем последний/первый элемент в очереди и записываем новый
    }
    
    return default;
}
```

# Полезные ссылки

Надеюсь, теперь стало понятно, почему пере`await`'ить `ValueTask` плохая затея, и как работают `IValueTaskSource`.
Если кому-то стала интересна эта тема, то прилагаю полезные ссылки:

- [Немного про `ValueTask`](https://habr.com/ru/articles/458828/)
- [`ManualResetValueTaskSourceCore`](https://github.com/dotnet/runtime/blob/a2c19cd005a1130ba7f921e0264287cfbfa8513c/src/libraries/Microsoft.Bcl.AsyncInterfaces/src/System/Threading/Tasks/Sources/ManualResetValueTaskSourceCore.cs#L22C26-L22C26)
- [`AwaitableSocketAsyncEventArgs`](https://github.com/dotnet/runtime/blob/a2c19cd005a1130ba7f921e0264287cfbfa8513c/src/libraries/System.Net.Sockets/src/System/Net/Sockets/Socket.Tasks.cs#L919)
- [`AsyncOperation`](https://github.com/dotnet/runtime/blob/ee2355c801d892f2894b0f7b14a20e6cc50e0e54/src/libraries/System.Threading.Channels/src/System/Threading/Channels/AsyncOperation.cs)
- [Статья, с которой скопировал реализацию](https://tooslowexception.com/implementing-custom-ivaluetasksource-async-without-allocations/)
