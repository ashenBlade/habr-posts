# OpenTelemetry .NET

Содержание:
1. Вступление
    - Описание OTEL
    - Мотивация
2. Сравнение OTEL и DiagnosticSource
    - Таблица сравнения
3. Трейсинг с помощью DiagnosticSource
    - Создание источника
    - Создание активности
    - Добавление тега
    - Добавление события
    - Изменение статуса
    - Использование `Tracer`
4. Подключение библиотеки и настройка в `ASP.NET Core`
    - Настройка окружения (название приложения, версия, тэги процесса)
        - Константы
        - `Detector`
        - Встроенные детекторы переменных окружения
    - Добавление инструментаторов
    - Добавление собственных источников
    - Несколько экспортеров
5. Рецепты:
    - Синхронный запрос от одного сервиса к другому по HTTP
    - Проброс контекста между различными приложениями - (`PropagationContext`, `Propagators`)
    - Добавление тегов в существующую `Activity` (`Activity.Current`)
    - Дробление большого запроса на несколько

## Сравнение OpenTelemetry и System.Diagnostics

Начиная с .NET 5 были добавлены типы, которые позволяют производить трейсинг работы приложения без необходимости подключения дополнительных библиотек. Речь о `Activity`, `ActivitySource`, `ActivityListener`. Они коррелируют с понятиями определенными в OpenTelemetry

| .NET            | OpenTelemetry | Комментарий                                                 |
| --------------- | ------------- | ----------------------------------------------------------- |
| Activity        | Span          | Операция, производимая приложением (бизнес-логика, запросы) |
| ActivitySource  | Tracer        | Создатель Activity/Span                                     |
| Tag             | Attribute     | Метаданные спана/операции                                   |
| ActivityKind    | SpanKind      | Взаимоотношения между зависимыми спанами                    |
| ActivityContext | SpanContext   | Контекст выполнения, который можно передать в другие спаны  |
| ActivityLink    | Link          | Ссылка на другой спан                                       |

Для приведения к единому знаменателю в OpenTelemetry добавили обертки вокруг System.Diagnostics, который работает с понятиями из OpenTelemetry. Эти типы объединены под одним понятием `Tracing shim`

| System.Diagnostics | Tracing shim  |
| ------------------ | ------------- |
| Activity           | TelemetrySpan |
| ActivitySource     | Tracer        |
| ActivityContext    | SpanContext   |

Но все же, создатели библиотеки рекомендуют пользоваться `System.Diagnostics` вместо `Tracing shim`. Дальше буду использовать `System.Diagnostics`

[Таблица сравнений](https://gist.github.com/lmolkova/6cd1f61f70dd45c0c61255сравнение039695cce8)

## Трейсинг в System.Diagnostics

Для трейсинга в .NET используется Activity API, предоставляемый `System.Diagnostics`.

Алгоритм работы с ним следующий:

1. Определяем источник событий: `ActivitySource`
```csharp
// TODO
```

2. В интересуемой операции создаем начинаем отслеживание новой активности
```csharp
// TODO
```

3. Добавляем метаданные
```csharp
// TODO
```

4. Заканчиваем событие
```csharp
// TODO
```

Как полученная информация обрабатывается - лежит на `ActivityListener`, но это ~~уже другая история~~ делает подключеная библиотека.

Лайфхаки/замечания:
- Метод `StartActivity` может вернуть `null`, если никто не подписан на событие. *При всех вызовах методов нужно делать проверку на `null`*
- `Activity` реализует `IDisposable`. *Можно не вызывать `Stop` вручную, а использовать `using`*
- `Activity` позволяет делать записи о пользовательских событиях: `activity.AddEvent(new ActivityEvent("Что-то случилось"))`. Дополнительно в библиотеке есть метод расширения для записи исключений: `activity.RecordException(ex)`
- Раз мы можем кидать/записывать исключения, то надо уметь отслеживать место, где исключение было брошено. Для этого можно выставить статус спана: `activity.SetStatus(ActivityStatusCode.Error, ex.Message)`. Под капотом, `activity.RecordException(ex)` работает через этот метод. (Кроме `Error` есть `Ok` и `Unset`)
- У каждой активности есть `Baggage` - коллекция ассоциированных с операцией данных. Разница в том, что `Baggage` может использоваться логикой приложения и передается между контекстами (сериализуется), а `Tag` - это просто метаданные для расследования инцидентов. Доступ ведется через одноименное свойство: `activity.Baggage`

Вот пример полного пути выполнения:
```csharp
```

## Подключение OpenTelemetry

Теперь разберемся как подключать OpenTelemetry в проект. 
Все библиотеки OpenTelemetry имеют префикс OpenTelemetry.
Для подключения базовой функциональности в ASP.NET Core необходимо подключить:
```shell
dotnet add package OpenTelemetry
dotnet add package OpenTelemetry.Extensions.Hosting 
```

Первая команда подключает функциональность OpenTelemetry, а вторая - методы расширения для регистрации сервисов.

> Многие библиотеки OpenTelemetry находятся в `prerelease` статусе, поэтому в менеджере пакетов просто так не отобразятся

Следующий этап - включить функциональность OpenTelemetry в ASP.NET Core

Это можно сделать методом расширения `AddOpenTelemetry()`

Но он только добавляет SDK в провайдер сервисов. Для дальнейшей настройки необходимо настроить сам трейсинг: `AddOpenTelemetry().WithTracing(...)`

Этот метод принимает лямбду для настройки трейсинга в приложении:
- Выставление метаинформации о приложении
- Подключение экспортеров трейсов
- Подписка на интересующие события

1. Выставление метаинформации о приложении

Для представления информации о чем-либо используется класс `Resource`. По факту это просто список пар ключ-значение. Информация о приложении выставляется через него. Точнее через `ResourceBuilder`, для которого в итоге вызывается `Build()` и получается результирующий `Resource`.

Для работы можно использовать 2 варианта:
- `SetResourceBuilder(ResourceBuilder builder)` - вручную выставляем нашего `ResourceBuilder` с выставленными значениями 
- `ConfigureResource(Action<ResourceBuilder> configure)` - лямбда с настройкой стандартного `ResourceBuilder`.

Информацию можно задать несколькими способами:
- `AddService` - вручную задать название, версию, ID инстанса
- `AddAttributes` - задать данные приложения в виде перечисления пар ключ-значение
- `AddDetector` - получить информацию о приложении через переданный детектор (возможно получение детектора из переменных окружения)
- `AddEnvironmentVariableDetector` - задать информацию о приложении через стандартные переменные окружения: OTEL_RESOURCE_ATTRIBUTES, OTEL_SERVICE_NAME

> Создание детектора


> Добавить ссылку на строку 

Вот пример конфигурирования сервиса температуры:
```cs
tracing.ConfigureResource(rb =>
{
   rb.AddService(
       serviceName: "TemperatureApi",
       serviceVersion: "1.0.1",
       autoGenerateServiceInstanceId: true);
   rb.AddEnvironmentVariableDetector();
   rb.AddDetector(sp =>
   {
        return new RandomSeedDetector(sp.GetRequiredService<IOptions<RandomOptions>>())
   });
})
```

Дальше необходимо настроить инструментаторы. 
Вкратце, инструментатор - это библиотека, которая позволит собирать трейсы из других библиотек без необходимости настраивать это самому.
Примером может служить `HttpClient`.

Для его инструментирования есть библиотека `OpenTelemetry.Instrumentation.Http`. 
Она за вас проставит необходимые метаданные для проброса контекста при отправке запросов `HttpClient`.

Подключить его можно методом расширения
```csharp
tracing.AddHttpClientInstrumentation()
```

Примеры других инструментаторов:
- `OpenTelemetry.Instrumentation.GrpcNetClient`
- `OpenTelemetry.Instrumentation.AspNetCore`
- `OpenTelemetry.Instrumentation.EntityFrameworkCore`
- `OpenTelemetry.Instrumentation.Runtime`
- `OpenTelemetry.Instrumentation.StackExchangeRedis`

Дальше необходимо зарегистрировать свои источники событий. Для этого есть метод `AddSource(params string[] names)`. На вход он принимает названия `ActivitySource`. 

Моя практика работы следующая:
- Создаю статический класс с `ActivitySource` (класс обычно назваю `Tracing`)
- В этом классе определяю названия активностей приложения (строковые константы)
- Когда начинается новая активность - обращаюсь к необходимому источнику и константе активности: `using var activity = Tracing.ApplicationActivity.StartActivity(Tracing.SampleOperation);`

Поэтому регистрация источников событий в `AddSource` выглядит как перечисление всех названий `ActivitySource` из всех подобных "классов-реестров":

```csharp
tracing.AddSource(FirstModule.Tracing.ApplicationActivity.Name, SecondModule.Tracing.AnotherActivity.Name);
```

Спаны собираются - хорошо. Но нужно их куда-то отправить. За это отвечают экспортеры.

Хоть это и OpenTelemetry библиотека, но экспортировать можно не только в OTEL формате.
Также есть поддержка (не только):
- Jaeger
- Zipkin
- Stackdriver

Подключение также тривиально. Например для подключения OpenTelemetry экспортера:
1. Добавляем пакет с экспортером

```shell
dotnet add package OpenTelemetry.Exporter.OpenTelemetryProtocol
```

2. Подключаем экспортер вызовом метода

```csharp
tracing.AddOltpExporter();
```

3. Настраиваем экспортера

```csharp
tracing.AddOltpExporter(oltp => 
{
    oltp.Endpoint = new Uri("http://oltp:4317");
});
```

Все этапы вместе:
```csharp
builder.Services
       .AddOpenTelemetry()
       .WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation()
                   .AddOtlpExporter(oltp =>
                    {
                        oltp.Endpoint = new Uri("http://oltp:4317");
                    })
                   .ConfigureResource(rb =>
                    {
                        rb.AddService(
                            serviceName: "TemperatureApi",
                            serviceVersion: "1.0.1",
                            autoGenerateServiceInstanceId: true);
                        rb.AddEnvironmentVariableDetector();
                        rb.AddDetector(sp =>
                            new RandomSeedDetector(sp.GetRequiredService<IOptions<RandomOptions>>()));
                    })
                   .AddHttpClientInstrumentation();
        });
```

## Рецепты

Для примера я сделал небольшой стенд из 3 сервисов с единственной операцией (запросом):
- OpenTelemetry.Web - принимает запрос от пользователя, делает HTTP запрос к OpenTelemetry.Temperature.Api и отправляет полученный объект в очередь кафки
- OpenTelemetry.Temperature.Api - простой HTTP API с единственной ручкой `temperature/current`, который возвращет рандомное число (температуру)
- OpenTelemetry.RecordSaver.Worker - фоновый процесс, который читает из кафки сообщения, отправляемые OpenTelemetry.Web, и сохраняет их в Postgres с помощью EF Core.

Для визуализации использовал Jaeger.

1. Синхронный запрос от одного сервиса к другому

Синхронный запрос в цепочке - от Web к Temperature.Api. Он выполняется с помощью `HttpClient`. Для отслеживания запросов в Web добавлен инструментатор HttpClient, а в Temperature.Api - AspNetCore инструментатор.

Внутри контроллера `Web` вызывается `ITemperatureService.GetTemeratureAsync()`, который делает HTTP запрос в `Temperature.Api`.

Эта часть отображена в трейсе:

![Трейс синхронного запроса](images/Снимок%20экрана%20от%202023-06-17%2016-13-43.png)

Первая часть принадлежит инструментатору HttpClient на Web, а вторая - инструментатору AspNetCore на Temperature.Api

2. Проброс контекста между различными приложениями - (`PropagationContext`, `Propagators`)

Что делать, если для какого-то варианта взаимодействия нет своего инструментатора? Как в этом случае передавать контекст?

В этих случаях нужно самим его передавать.

Для этого предназначен `Propagators API`.
Он предоставляет фреймворк для передачи контекста.
Транспортный слой передачи определяет сам пользователь - можно передавать где захочешь.

Передающая сторона:
1. Получает инстанс `Propagator`. На данный момент есть только `TextMapPropagator`, который использует строковое отображение, но планируется добавление варианта передачи по байтам.

2. Вызывает метод `Inject<T>(PropagationContext context, T carrier, Action<T,string,string> setter)`. `T carrier` - это тип используемого хранилища, а `Action<T,string,string> setter` - функция для добавления данных в хранилище.
   
3. Делает необходимый запрос

Получающая сторона:
1. Получает инстанс `Propagator`
2. Получает хранилище из запроса
3. Вызывает `Extract<T>(PropagationContext context, T carrier, Func<T,string,IEnumerable<string>> getter)`. `getter` используется уже для получения данных из хранилища
4. При создании новой активности использует полученные данные:
   - Установка `Baggage`
   - Выставление родительнского контекста
   - Добавление ссылок

Хватит теории, давайте практику.
К сожалению (или счастью), для кафки я не нашел инструментатора. Поэтому написал свои декораторы, которые пробрасывают контекст.

Продьюсер:
```csharp
public class TracingProducerDecorator<TKey, TValue>: IProducer<TKey, TValue>
{
    private readonly IProducer<TKey, TValue> _producer;

    public TracingProducerDecorator(IProducer<TKey, TValue> producer)
    {
        _producer = producer;
    }

    private const string ProducingActivity = "Kafka.Producer.Produce";

    private Activity? StartActiveSpan(Message<TKey, TValue> message)
    {
        var activity = Tracing.WebActivitySource.StartActivity(ProducingActivity, ActivityKind.Producer);
        if (activity is not null)
        {
            var propagationContext = new PropagationContext(activity.Context, Baggage.Current);
            Propagators.DefaultTextMapPropagator.Inject(propagationContext, message.Headers ??= new Headers(),
                (headers, key, value) => headers.Add(key, Encoding.UTF8.GetBytes(value)));
        }
        return activity;
    }

    public async Task<DeliveryResult<TKey, TValue>> ProduceAsync(string topic, Message<TKey, TValue> message, CancellationToken cancellationToken = new CancellationToken())
    {
        using var activity = StartActiveSpan(message);
        try
        {
            var result = await _producer.ProduceAsync(topic, message, cancellationToken);
            activity?.SetTag("kafka.topic", result.Topic);
            activity?.SetTag("kafka.partition", result.Partition.Value);
            activity?.SetTag("kafka.offset", result.Offset.Value);
            return result;
        }
        catch (Exception e)
        {
            activity.RecordException(e);
            activity.SetStatus(Status.Error);
            throw;
        }
    }

    public void Produce(string topic, Message<TKey, TValue> message, Action<DeliveryReport<TKey, TValue>> deliveryHandler = null!)
    {
        var span = StartActiveSpan(message);
        try
        {
            _producer.Produce(topic, message, (r) =>
            {
                try
                {
                    if (r.Error.IsError)
                    {
                        span?.SetStatus(ActivityStatusCode.Error, $"Ошибка кафки: {r.Error.Reason}");
                    }
                    else
                    {
                        span?.SetTag("kafka.topic", r.Topic);
                        span?.SetTag("kafka.partition", r.Partition.Value);
                        span?.SetTag("kafka.offset", r.Offset.Value);
                    }
                    span?.Dispose();
                }
                catch (ObjectDisposedException)
                { }
                deliveryHandler(r);
            });
        }
        catch (Exception e)
        {
            span?.RecordException(e);
            span?.SetStatus(Status.Error);
            span?.Dispose();
            throw;
        }
    }
}
```

Консьюмер:
```csharp
private static IEnumerable<string> GetValuesFromHeadersSafe(Headers headers, string key)
    => headers.Where(x => x.Key == key)
                .Select(b =>
                {
                    try
                    {
                        return Encoding.UTF8.GetString(b.GetValueBytes());
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                })
                .Where(x => x is not null)!;


private Activity? StartActivity(ConsumeResult<Null, string> result)
{
    var propagationContext = Propagators.DefaultTextMapPropagator.Extract(default, result.Message.Headers,
        GetValuesFromHeadersSafe);

    var span = Tracing.ConsumerActivitySource.StartActivity(
        Tracing.KafkaMessageProcessing, 
        kind: ActivityKind.Consumer,
        parentContext: propagationContext.ActivityContext);

    Baggage.Current = propagationContext.Baggage;
    
    return span;
}
```

Если мы попробуем положить в `Baggage` какие-нибудь данные, то получим на обратной стороне.
Для приложений из стенда можно указать специальные переменные окружения:
- `TRACING_SEND_RANDOM_BAGGAGE=true` для Web, чтобы посылал случайные данные
- `TRACING_LOG_BAGGAGE=true` для RecordSaver.Worker, чтобы логировал получаемый Baggage

Логи продьюсера:
![](images/Логирование%20Baggage%20продьюсером.png)

Логи консьюмера:
![](images/Логирование%20Baggage%20консьюмером.png)

> Замечание: функции `getter` и `setter` у `Propagator` не должны выкидывать исключения

1. Добавление тегов в существующую `Activity` (`Activity.Current`)

Если в текущую `Activity` необходимо добавить информацию. Например, атрибуты или событие, то возникает вопрос как к ее получить.

Ответ прост - статическое свойство `Activity.Current`. Оно вернет текущий `Activity`, если он есть, иначе `null`.

> Для хранения `Activity`, используется статическое поле типа `AsyncLocal<Activity>`. Поэтому обращение к свойству из различных асинхронных функций вернет текущий `Activity`
```csharp
private static readonly AsyncLocal<Activity?> s_current = new AsyncLocal<Activity?>();
public static Activity? Current
{
    get { return s_current.Value; }
    set
    {
        if (ValidateSetCurrent(value))
        {
            SetCurrent(value);
        }
    }
}
```

Например, мы хотим сделать декоратор для какого-то сервиса, который будет добавлять событие при возникновении особенного исключения.

Для примера, сделал декоратор для `ITemperatureService`, который добавляет событие ошибки парсинга JSON

```csharp
public class JsonExceptionEventRecorderServiceDecorator: ITemperatureService
{
    private readonly ITemperatureService _service;

    public async Task<double> GetTemperatureAsync(CancellationToken token)
    {
        try
        {
            return await _service.GetTemperatureAsync(token);
        }
        catch (JsonException e) when (Activity.Current is {} activity)
        {
            var @event = new ActivityEvent("Ошибка парсинга JSON",
                tags: new ActivityTagsCollection(new KeyValuePair<string, object?>[]
                {
                    new("json.error.path", e.Path)
                }));
            activity.AddEvent(@event);
            throw;
        }
    }
}
```

1. Дробление большого запроса на несколько

Батч операции могут оптимизировать работу системы, но иногда один большой батч надо дробить на несколько меньше. 
В примере я использую того же самого `IProducer<TKey, TValue>` с декоратором описанным ранее.

Добавил новую ручку `System/state/batch`, которая делает буквально то же самое, но отправляет несколько запросов параллельно через `Task.WhenAll()`

```csharp
var measurements = Enumerable.Range(0, amount)
                             .Select(_ => new WeatherForecast()
                             {
                                 Id = Guid.NewGuid(),
                                 Date = DateTime.Now,
                                 Summary = Faker.Random.Words(5),
                                 TemperatureC = temp
                             })
                             .ToArray();

await Task.WhenAll(measurements.Select(m => _producer.ProduceAsync("weather", new Message<Null, string>()
{
    Value = JsonSerializer.Serialize(m)
},
token)));
```

Зависимости между спанами корректно обрабатываются

![](images/Параллельные%20запросы%20батчи.png)


1. Асинхронное взаимодействие

Представим, что мы работает с кафкой. Как известно из одного топика могут читать несколько групп потребителей. У нас есть happy-path - цепочка сервисов занимающаяся непосредственной бизнес-логикой. Другая часть - вспомогательная: синхронизация представлений, аудит, составление отчетов, другие сервисы экосистемы.

Как нам знать, что прочитанное сообщение относится к этому трейсу/спану? Самый простой вариант - добавить контекст как родительский. Но в этом случае трейсинг замусорится лишними операциями.

Для обработки таких ситуаций в OpenTelemetry ввели `Link`. По факту, это просто метаинформация о корреляции с другим трейсом/спаном.

В .NET его можно представить в виде


