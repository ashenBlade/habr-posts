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

|.NET|OpenTelemetry|Комментарий|
|-|-|-|
|Activity|Span|Операция, производимая приложением (бизнес-логика, запросы)|
|ActivitySource|Tracer|Создатель Activity/Span|
|Tag|Attribute|Метаданные спана/операции|
|ActivityKind|SpanKind|Взаимоотношения между зависимыми спанами|
|ActivityContext|SpanContext|Контекст выполнения, который можно передать в другие спаны|
|ActivityLink|Link|Ссылка на другой спан|

Для приведения к единому знаменателю в OpenTelemetry добавили обертки вокруг System.Diagnostics, который работает с понятиями из OpenTelemetry. Эти типы объединены под одним понятием `Tracing shim`

|System.Diagnostics|Tracing shim|
|-|-|
|Activity|TelemetrySpan|
|ActivitySource|Tracer|
|ActivityContext|SpanContext|

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

Для примера я сделал небольшой стенд из 3 сервисов:
- OpenTelemetry.Web - принимает запросы от пользователя, делает запрос к OpenTelemetry.Temperature.Api и отправляет полученный объект в очередь кафки
- OpenTelemetry.Temperature.Api - простой HTTP API с единственной ручкой `temperature/current`, который возвращет рандомное число (температуру)
- OpenTelemetry.RecordSaver.Worker - фоновый процесс, который читает из кафки сообщения, отправляемые OpenTelemetry.Web, и сохраняет их в Postgres с помощью EF Core.

1. Синхронный запрос от одного сервиса к другому




