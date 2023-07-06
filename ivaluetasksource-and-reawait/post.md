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
Например, возврат закэшированного результата, или возврата ValueTask.FromCancelled с переданным CancellationToken.

Но время шло, аппетиты возрастали и одним ранним выходом теперь не обойтись.
Поэтому в .NET Core 2.1 был добавлен `IValueTaskSource`.

Теперь `ValueTask` можно создать не только передав готовый результат или `Task`, но 
и упомянутый выше `IValueTaskSource`.

```cs
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

    // Запланировать продолжение на выполнение по окончании операции
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
Статус представляет собой перечисление `ValueTaskSourceStatus`

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
>  - Фоновый поток не получил обратно управление, т.к. продолжение было вызвано и не выставил семафор

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

# Делаем полностью своими руками

Теперь сделаем свою реализацию. 
Представим, что у нас есть класс `PcMonitor`. 
Он вызывается очень часто и замеряет статистику компьютера.
Для оптимизации мы решили:
- Делать запросы не на каждый вызов, а с определенным интервалом и хранить полученные значения в кэшэ.
- Если при вызове значение из кэша еще актуально, то вернуть его, иначе ждать до следующего сбора.

<spoiler title="Детали реализации">

Статистика представляется структурой `PcStatistics`. 
Пока там только температура процессора, но вы придумайте, что туда еще можно добавить.

```cs
public readonly record struct PcStatistics(double CpuTemperature);
```

В реализации используется класс `ValueTaskSourcePcMonitor`.
Сбор статистики реализован с помощью `System.Threading.Timers.Timer`, 
который с определенным интервалом кладет в кэш новое значение и обновляет время сбора.

Время сбора представляет `TimeStamp`, получаемый с помощью `Stopwatch` (не самая лучшая идея, но сойдет)

</spoiler>

Реализация `IValueTaskSource` представляется классом `ManualValueTaskSource`.
Он хранит в себе необходимые для работы поля.
```cs
// Наш "отец"
private CustomPcMonitor? _monitor;

// Объекты для получения результата
private CancellationToken _cancellationToken;
private PcStatistics _cachedResult = new();
private Exception? _exception;

// Объекты для правильной работы IValueTaskSource
private object? _state;
private object? _scheduler;
private Action<object?>? _continuation;
private short _version;
private ExecutionContext? _ec;

// Вспомогательный объекты
private readonly Timer _timer;
private TimeSpan _lastMeasurementTime = TimeSpan.Zero;
```

`GetStatus`

Пожалуй, это самая простая реализация
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

Все приведенные выше методы довольно просты в реализации, но в прод их не принесешь:
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

Реализацию написали. Мы молодцы. А теперь все выбрасываем, так как реализация за нас уже сделана - `ManualResetValueTaskSource`.
Она реализует все выше приведенные 

Реализация по большей части шаблонная.

Конкурентная реализация уже есть в `ManualResetValueTaskSourceCore`.
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
В `PcMonitor` добавим пул этих объектов и будем забирать при вызове `GetStatisticsAsync`, а при вызове `GetResult` будем возвращать себя обратно в пул.

```cs
// ValueTaskSource
public PcStatistics GetResult(short token)
{
    try
    {
        return _source.GetResult(token);
    }
    finally
    {
        // Возвращаем в пул
        _pool.Return(this);
    }
}

// PcMonitor
public ValueTask<PcStatistics> GetStatisticsAsync(CancellationToken token = default)
{
    // Берем из пула
    var source = _pool.Get();
    return source.Start(this, _pool, token);
}
```

`GetResult` вызывается всегда один раз - при получении результата. 
При этом наш `ValueTaskSource` возвращается обратно в пул.

Теперь мы можем создать ограниченное количество `ValueTaskSource` и постоянно их переиспользовать без лишних аллокаций памяти!

Но что, если кто-то попытается за`await`'ить `ValueTask` несколько раз? 
Даже если эти вызовы были крайне близки во времени, никто не гарантирует, что за это время 
тот же самый `ValueTaskSource` не будет передан в другой `ValueTask` и хранить новое значение.

Вот тут и нужен `short token`, передававшийся в любой метод. 
Он призван проверять, что вызвавший код, обращается к `ValueTask`, в которой находится актуальный `ValueTaskSource`.
В `ManualResetValueTaskSourceCore` он реализован в виде простого счетчика, поэтому там он называется `Version`.
Но в общем случае это не обязательно - сойдет любое неповторяющееся значение.

# Как реализован сокет с помощью него (все делают пример на нем, я не исключение)

`IValueTaskSource` (как говорят) был добавлен специально для сокетов.
Все делают примеры на нем - я не исключение.

Реализация -  `AwaitableSocketsEventArgs`

Пример на ReceiveAsync.
Используется один буфер, т.к. чтение только 1 потоком возможно.
Строка 307

Строка 953 - если операция не завершилась - передача самого себя

Внутри все доходит до нативной операции и происходит возврат

Внутри `SocketAsyncEventArgs` (базовом классе) есть переменные для каждой возможной операции.
Начало класса

Сделать аналогию с нашей реализацией

# Бенчмарки?

Сравнение памяти ValueTask и Task

# Полезные ссылки

- Мой проект
- Реализация сокета (гитхаб)
- 

