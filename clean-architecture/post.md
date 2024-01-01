# Введение

Написано под влиянием книги Мартина Фаулера "Чистая архитектура" и часто задаваемых вопросов.

Написано для:
- Junior разработчиков
- Разработчиков, которые хотят устроить холи-вар
- Разработчиков, которые хотят поделиться своим мировоззрением чистой архитектуры

# Что такое чистая архитектура

Вкратце, чистую архитектуру можно описать фразой `Раздели Бизнес Логику и работу с внешним миром`.
То есть мы сначала моделируем предметную область и только потом обкладываем его всякими контроллерами, БД, HTTP клиентами и др.

В частности это может означать:
1. Нет никаких контроллеров/презентеров;
2. Мы не знаем ни о какой базе данных - только об абстрактном хранилище чего-либо;
3. Как мы работаем с другими сервисами не известно - просто вызываем функции/методы.

> Замечание: пункт 2 не означает, что ВСЯ логика на стороне кода. Объяснение дальше. 

<spoiler title="Domain-first">

В контексте работы с БД есть 3 подхода:
- Code first
- Database first
- Model first

В контексте API сервисов:
- Code first
- Contract first

Для подхода чистой архитектуры я бы дал название `Domain-first`

<spoiler>


TODO: про книгу написать

TODO: показать диаграмму + пояснить, что там презентер, но в вебе другое представление и надо пояснить что да как

# Чистая архитектура на примере

## Предметная область

Чтобы стало понятнее, создадим свой проект, используя идею чистой архитектуры.
В качестве предметной области выберем бронирование билетов в кинотеатре.


Первым делом выделим очертания нашей предметной области: как она выглядит и по каким правилам живет.

*Процесс сбора требований. 3.. 2.. 1.. Требования собраны* 

Поговорим с кассиром, бухгалтером и администратором мы выделили следующие требования:
- Все места на начало сеанса свободные;
- Клиент может забронировать свободное место;
- Клиент может купить свободное место, либо место, которое было забронировано им самим;

*Процесс моделирования предметной области*

TODO: отрефакторить

Итак мы собрали требования и выделили правила нашей предметной области:
- У нас есть только 1 кинотеатр;
- В кинотеатре есть множество залов для просмотра кино;
- Каждый зал пронумерован начиная с 1;
- У каждого зала есть свое количество мест;
- Для каждого зала есть свое расписание показа фильмов. Промежуток времени, когда в зале показывается фильм, называется сеансом;
- Время сеансов для одного и того же расписания не должны пересекаться;
- 1 место для каждого сеанса может быть куплено только 1 посетителем;
- Посетитель может забронировать место для сеанса, только если место свободно;
- Время одного сеанса равно длительности фильма;
- Промежуток между сеансами не может быть меньше 30 минут;
- Если место забронировано, то купить его может только тот, кто это место забронировал.

По полученным правилам можно создать такую диаграмму, чтобы понять, кто будет присутствовать в этой модели:

TODO: UML диаграмма

<spoiler title="Правила в кратце">

TODO: мб заменить на это?

Все правила можно описать следующим образом:
- Есть несколько залов, которые показывают кино по своим расписаниям;
- Человек может либо сразу купить место, либо сначала забронировать и потом купить;
- Каждое место для одного и того же сеанса может быть занято максимум 1 человеком;
- Сеансы в одном и том же зале не должны пересекаться, т.е. идут строго последовательно.

</spoiler>

> Стоит заметить, что мы моделируем только бронирование/покупку мест: за то как формируются сеансы, добавляются новые фильмы, как принимается оплата (и есть ли она вообще) и др. мы не отвечаем - это не наша обязанность.

## Моделирование предметной области

Когда стало понятнее, с чем мы имеем дело, можно приступать к моделированию в коде.

Наша главная задача - бронировать и покупать места для посетителей на сеансах.
Сеанс содержит в себе интервал, когда он действует, фильм, показ которого ведется, и сами места.
В коде, его можно реализовать следующим образом:

```csharp
class Session
{
    /// <summary>
    /// Идентификатор сеанса
    /// </summary>
    public int Id { get; }
    
    /// <summary>
    /// Фильм, который показывается
    /// </summary>
    public int MovieId { get; }

    /// <summary>
    /// Интервал длительности сеанса, 
    /// включая время самого фильма и время обслуживания
    /// </summary>
    public SessionInterval Interval { get; }

    private readonly Seat[] _seats;
    
    /// <summary>
    /// Места для этого сеанса 
    /// </summary>
    public IReadOnlyCollection<Seat> Seats => _seats;
    
    public Session(SessionInterval interval, int movieId, IEnumerable<Seat> seats)
    {
        ArgumentNullException.ThrowIfNull(interval);
        ArgumentNullException.ThrowIfNull(seats);
        
        Interval = interval;
        MovieId = movieId;
        _seats = BuildSeatsArray(seats);
    }

    private static Seat[] BuildSeatsArray(IEnumerable<Seat> seats)
    {
        var array = seats.ToArray();
        for (var i = 0; i < array.Length; i++)
        {
            if (array[i] == null!)
            {
                throw new ArgumentNullException(nameof(seats), $"Объект места на {i} позиции был null");
            }
        }

        return array;
    }
    
    /*
     * Будет дальше
     */
}
```

> Здесь присутствует класс SessionInterval - простая обертка над парой DateTime (начало и конец). 
> Ее преимущество также в том, что автоматически проверяется пересечение времени начала и конца.

Единственный способ нарушить состояние — самим накосячить:
- Создание объекта через вызов конструктора, где проверяются все входные параметры;
- В коде просто так состояние не изменить — все свойства неизменяемые (get-only, IReadOnlyCollection);

Вы могли заметить, что для мест используется класс `Seat`, но его определения еще не было, — это далее.

Место может быть в 3 состояниях: свободно, забронировано и куплено.
Причем, если место забронировано или куплено, то ЗА КЕМ-ТО.

Если бы мы сначала думали про БД, то реализовали как-то так:

```csharp
enum SeatType
{
    Free = 0,
    Booked = 1,
    Bought = 2,
}

class Seat
{
    public SeatType Type { get; set; }
    public int SessionId { get; set; }
    public Session Session { get; set; }
    public int Number { get; set; }
    public int? ClientId { get; set; }
}
```

Это простой POCO класс, где вся реализация видна. 
Мы, конечно, могли бы добавить пару методов для дополнительных проверок и логики.
Например, для получения Id клиента, когда место забронировано:

```csharp
class Seat
{
    public bool TryGetClientIdBooked(out int clientId)
    {
        if (Type == SeatType.Booked)
        {
            clientId = ClientId.Value;
            return true;
        }
        clientId = default;
        return true;
    }
}
```

Но этот подход плох тем, что:
1. Состояние может изменить любой желающий и полагаться на добросовестность разработчиков не стоит: можно случайно выставить Id клиента в `null` для купленного места;
2. У разных состояний обязательно должны быть поля, которые ему не нужны: например, Id клиента для свободного места;

Будем исправлять эти проблемы поэтапно и начнем с 2 пункта.
Можно заметить, что каждое состояние можно представить отдельным классом - без необходимости перечисления типа.
Перепишем следующим образом:

```csharp
abstract class Seat
{
    /// <summary>
    /// Номер места в зале
    /// </summary>
    public int Number { get; }
    protected internal Seat(int number)
    {
        if (number < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number, "Номер места должно быть положительным");
        }
        Number = number;
    }
} 

class FreeSeat: Seat
{
    public FreeSeat(int number) : base(number)
    { }
}

class BookedSeat: Seat
{
    /// <summary>
    /// Клиент, который забронировал место
    /// </summary>
    public int ClientId { get; }

    public BookedSeat(int number, int clientId) : base(number)
    {
        ClientId = clientId;
    }
}

class BoughtSeat: Seat
{
    /// <summary>
    /// Клиент, который купил место
    /// </summary>
    public int ClientId { get; }

    public BoughtSeat(int number, int clientId) : base(number)
    {
        ClientId = clientId;
    }
}
```

Стоит заметить следующее:
- Для каждого состояния места отдельный класс, поэтому проблем с перечислением нет;
- Можно утверждать, что типы мест зафиксированы - конструктор помечен `internal`, поэтому подтипы могут располагаться только в сборке с доменными сущностями и никто свинью не подложит;
- Есть проверка на корректность номера места;
- Теперь у каждого места есть поля, которые нужны только ему;
- Нет необходимости хранить служебные поля для схемы БД (`Session` для связи места с сеансом).

Пункт 1 исправился на ходу: все объекты стали неизменяемы, поэтому случайно сломать не получится.

Остался только вопрос с определением типа места.
Сделать это можно несколькими способами. Первое, что приходит на ум - рефлексия:
```csharp
public void DoSomething(Seat seat)
{
    switch (seat)
    {
        case FreeSeat freeSeat:
            // ...
        case BookedSeat bookedSeat:
            // ...
        case BoughtSeat boughtSeat:
            // ...
        default:
            throw new ArgumentException("Неизвестный тип места");
    }
}
```

Но этот путь мне не нравится:
- Каждый метод/функция будет сильно раздуваться;
- При добавлении нового типа места необходимо будет найти ВСЕ точки использования этого `switch`;
- Рефлексия сама по себе немного медленная + проблемы в AOT (это уже .NET).

Я выбрал другой способ - паттерн Посетитель.
TODO: ссылка на этот паттерн.

Реализуем его:
```csharp
public interface ISeatVisitor<out T>
{
    T Visit(FreeSeat freeSeat);
    T Visit(BoughtSeat boughtSeat);
    T Visit(BookedSeat bookedSeat);
}

abstract class Seat
{
    public abstract T Accept<T>(ISeatVisitor<T> visitor);
}

class FreeSeat: Seat
{
    public override T Accept<T>(ISeatVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}


class BookedSeat: Seat
{
    public override T Accept<T>(ISeatVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}

class BoughtSeat: Seat
{   
    public override T Accept<T>(ISeatVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}
```

Вот и все, дело за малым - бизнес-логика.
Мы должны поддерживать 2 операции - бронирование и покупка мест.

Для этого определим отдельный сервис - `SeatService`. 
В нем будут содержаться наши варианты использования (Use Cases).
У нас их всего 2: бронирование и покупка мест.

```csharp
class SeatService
{
    public Task BookSeatAsync(int sessionId, int place, int clientId, CancellationToken token)
    {
        // ...
    }
    
    public Task BuySeatAsync(int sessionId, int place, int clientId, CancellationToken token)
    {
        // ...
    }
}
```

Теперь нужно понять, какая для каждого метода должна быть логика.
Вот здесь уже вступает в игру бизнес-логика.

Напомним эти правила:
1. Забронировать можно только свободное место.
2. Купить можно:
   - либо свободное место
   - либо забронированное этим же посетителем место

Перед реализацией логики нужно подумать откуда брать само состояние? 
Для этого выделим отдельный сервис-хранилище этих данных: `ISessionRepository`.
```csharp
interface ISessionRepository
{
    public Task<Session> GetSessionByIdAsync(int sessionId, CancellationToken token = default);
    public Task UpdateSeatAsync(int sessionId, Seat seat, CancellationToken token = default);
}
```

От него нам нужно только 2 действия:
- Получить сеанс со всеми его местами 
- Обновить информацию о месте

На этом можно приступить к реализации. 
Начнем с бронирования. 
Эта логика будет содержаться внутри самого сеанса.

```csharp
class Session
{
    public bool TryBook(int place, int clientId, out BookedSeat bookedSeat)
    {
        var (seat, index) = FindSeatByPlace(place);
        var visitor = new BookingSeatVisitor(this, index, clientId);
        if (seat.Accept(visitor) is {} s)
        {
            bookedSeat = s;
            return true;
        }

        bookedSeat = default!;
        return false;
    }
    
    /// <summary>
    /// Реализация <see cref="ISeatVisitor{T}"/>, который бронирует место.
    /// Возвращает:
    /// - Новый объект, если место было забронировано,
    /// - null - если место уже было забронировано этим же посетителем ранее
    /// </summary>
    private class BookingSeatVisitor : ISeatVisitor<BookedSeat?>
    {
        /// <summary>
        /// Сеанс, который мы обслуживаем
        /// </summary>
        public Session Parent { get; }
        /// <summary>
        /// Индекс в массиве <see cref="Session._seats"/>
        /// </summary>
        public int Index { get; }
        /// <summary>
        /// Клиент, для которого мы бронируем место
        /// </summary>
        public int ClientId { get; }

        public BookingSeatVisitor(Session parent, int index, int clientId)
        {
            Parent = parent;
            Index = index;
            ClientId = clientId;
        }
        
        public BookedSeat? Visit(FreeSeat freeSeat)
        {
            var bookedSeat = new BookedSeat(freeSeat.Number, ClientId);
            Parent._seats[Index] = bookedSeat;
            return bookedSeat;
        }

        public BookedSeat? Visit(BoughtSeat boughtSeat)
        {
            throw new SeatBoughtException(boughtSeat.ClientId);
        }

        public BookedSeat? Visit(BookedSeat bookedSeat)
        {
            if (bookedSeat.ClientId == ClientId)
            {
                return null;
            }

            throw new SeatBookedException(bookedSeat.ClientId);
        }
    }
}

class SeatService
{
    private readonly ISessionRepository _sessionRepository;

    public SeatService(ISessionRepository sessionRepository)
    {
        ArgumentNullException.ThrowIfNull(sessionRepository);
        _sessionRepository = sessionRepository;
    }

    public async Task BookSeatAsync(int sessionId, int place, int clientId, CancellationToken token = default)
    {
        var session = await _sessionRepository.GetSessionByIdAsync(sessionId, token);
        if (session.TryBook(place, clientId, out var bookedSeat))
        { 
            await _sessionRepository.UpdateSeatAsync(sessionId, bookedSeat, token);
        }
    }
}
```

Для реализации логики я воспользовался упомянутым ранее паттерном посетитель.

Также долго откладывался вопрос, что делать в случае нарушения правил бизнес-логики.
Я выбрал стратегию доменных исключений - когда нарушается какое-то бизнес-правило, то выкидывается соответствующее исключение.
Например, когда клиент хочет забронировать уже купленное место, то кидается `SeatBoughtException`.

Покупка мест выполняется аналогично.

## Работаем с внешним миром

### Хранилище

Бизнес-логика реализована. 
Теперь уже стоит подумать как мы будем работать с внешним миром.

Для нас это означает 1 вещь - реализация `ISessionRepository`.
По требованиям мы должны хранить данные в БД, поэтому и реализация будет ее использовать.

Во-первых, надо разработать схему того, как эти данные будут храниться в БД.
Я использую ORM (Entity Framework), поэтому покажу в виде классов.

> Для разграничения доменных моделей и моделей БД использую префикс `Database`

```csharp
public class DatabaseSession
{
    public int Id { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int MovieId { get; set; }
    public ICollection<DatabaseSeat> Seats { get; set; } 
}

public class DatabaseSeat
{
    public int SessionId { get; set; }
    public DatabaseSession Session { get; set; }
    public int Number { get; set; }
    public SeatType Type { get; set; }
    public int? ClientId { get; set; }
}
```

Здесь можно заметить разницу:
- Модель места такая же как и показывалась ранее - денормализованная и хранит ссылку на сеанс, которому принадлежит;
- Интервал сеанса хранится не как доменный объект, а в виде 2 дат - начала и окончания.

Теперь перейдем к самому хранилищу сеансов и мест `ISessionRepository`.

```csharp
public class PostgresSessionRepository: ISessionRepository
{
    private readonly SessionDbContext _context;

    public PostgresSessionRepository(SessionDbContext context)
    {
        _context = context;
    }
    
    public async Task<Session> GetSessionByIdAsync(int sessionId, CancellationToken token)
    {
        var found = await _context.Sessions
                                  .AsNoTracking()
                                  .Include(s => s.Seats)
                                  .FirstOrDefaultAsync(s => s.Id == sessionId, token);
        if (found is null)
        {
            throw new SessionNotFoundException(sessionId);
        }

        var interval = new SessionInterval(found.Start, found.End);
        var seats = found.Seats.Select(seat => seat.ToDomainSeat());
        return new Session(interval, found.MovieId, seats);
    }
    
    public async Task UpdateSeatAsync(int sessionId, Seat seat, CancellationToken token)
    {
        var databaseSeat = seat.Accept(new DatabaseSeatMapperSeatVisitor(sessionId));
        var updated = await _context.Seats
                                    .Where(s => s.SessionId == sessionId && s.Number == seat.Number)
                                    .ExecuteUpdateAsync(calls => calls.SetProperty(s => s.ClientId, databaseSeat.ClientId)
                                                                      .SetProperty(s => s.Type, databaseSeat.Type), token);
        if (updated == 0)
        {
            throw new SessionNotFoundException(sessionId);
        }
    }
}
```

Единственная обязанность хранилища - маппинг объектов.
Никакой бизнес-логики в нем не содержится.

TODO: ссылка на папку с реализацией

### Пользуемся

На последнее я оставил то, как пользователь будет взаимодействовать с нашей системой.
Выбирать мы можем любой способ - бизнес-логика ничего не знает о UI слое.

Представим, что мы делаем REST API. 
Тогда реализация будет примерно следующей.

```csharp
[ApiController]
[Route("sessions")]
public class SessionsController: ControllerBase
{
    private readonly ISeatService _seatService;

    public SessionsController(ISeatService seatService)
    {
        _seatService = seatService;
    }

    [HttpPut("{sessionId:int}/places/{placeId:int}/book")]
    public async Task<IActionResult> BookSeat(int sessionId, int placeId, [FromQuery][Required] int userId, CancellationToken token = default)
    {
        await _seatService.BookSeatAsync(sessionId, placeId, userId, token);
        return Ok();
    }
    
    [HttpPut("{sessionId:int}/places/{placeId:int}/buy")]
    public async Task<IActionResult> BuySeat(int sessionId, int placeId, [FromQuery][Required] int userId, CancellationToken token = default)
    {
        await _seatService.BuySeatAsync(sessionId, placeId, userId, token);
        return Ok();
    }
}
```

Вот и все - нам достаточно только получить этот `ISeatService` и вызвать нужный Use Case/Метод.

Осталось подумать, что делать с доменными исключениями. 
Можно в каждом методе контроллера прописывать `try/catch` блок, но я воспользуюсь возможностями ASP.NET Core и создам фильтр исключений.

```csharp
class DomainExceptionFilterAttribute: ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not DomainException domainException)
        {
            return;
        }
    
        var responseObject = new {message = domainException.Message};
        context.Result = domainException switch
                         {
                             SeatNotFoundException    => new NotFoundObjectResult(responseObject),
                             SessionNotFoundException => new NotFoundObjectResult(responseObject),
                             _                        => new BadRequestObjectResult(responseObject)
                         };
    
        context.ExceptionHandled = true;
    }
}

[DomainExceptionFilter]
public class SessionsController: ControllerBase
{
    // ...
}
```

Вот и все приложение готово!

# Тестирование

Как тестировать

> Все ниже можно в виде `Фичи/А зачем это все нужно` секции офомить с подпунктами

Теперь перейдем к фичам и замечаниями, которые ранее были опущены. 
Начнем с тестирования.

Хочется отметить, то что теперь бизнес-логика прекрасно тестируется - никаких внешних зависимостей делать не нужно, просто создадим моки.

Для примера, напишем тест на бронирования места.

```csharp
public class SeatServiceTests
{
    private static readonly SessionInterval StubInterval = new(new DateTime(2022, 1, 1), new DateTime(2022, 2, 1));
    private static readonly SeatEqualityComparer SeatComparer = new();
    
    [Fact]
    public async Task BookSeatAsync__WhenSeatIsFree__ShouldMarkSeatBooked()
    {
        var (sessionId, seatNumber, movieId, clientId) = ( 1, 1, 1, 2 );
        var expectedSeat = new BookedSeat(seatNumber, clientId); 
        var session = new Session(sessionId, StubInterval, movieId, new[] {new FreeSeat(seatNumber)});
        var sessionRepo = new StubSessionRepository(new[] {session});
        var service = new SeatService(sessionRepo);

        var actual = await service.BookSeatAsync(sessionId, seatNumber, clientId);
        
        Assert.Equal(expectedSeat, actual, SeatComparer);
    }

    [Fact]
    public async Task BookSeatAsync__WhenSeatIsBought__ShouldThrowSeatBoughtException()
    {
        var (sessionId, seatNumber, movieId, clientId, boughtClientId) = ( 1, 1, 1, 2, 10 );
        var session = new Session(sessionId, StubInterval, movieId, new[] {new BoughtSeat(seatNumber, boughtClientId)});
        var sessionRepo = new StubSessionRepository(new[] {session});
        var service = new SeatService(sessionRepo);

        await Assert.ThrowsAnyAsync<SeatBoughtException>(() => service.BookSeatAsync(sessionId, seatNumber, clientId));
    }

    [Fact]
    public async Task BookSeatAsync__WhenSeatIsBought__ShouldSpecifyCorrectClientIdInException()
    {
        var (sessionId, seatNumber, movieId, clientId, boughtClientId) = ( 1, 1, 1, 2, 10 );
        var session = new Session(sessionId, StubInterval, movieId, new[] {new BoughtSeat(seatNumber, boughtClientId)});
        var sessionRepo = new StubSessionRepository(new[] {session});
        var service = new SeatService(sessionRepo);

        var exception = (SeatBoughtException) ( await Record.ExceptionAsync(() => service.BookSeatAsync(sessionId, seatNumber, clientId)) )!;
        Assert.Equal(boughtClientId, exception.ClientId);
    }
}
```

> Замечание: не вся бизнес-логика теперь находится в наших руках. 
> Например, гораздо более эффективно отслеживать дублирование Id в самом хранилище, нежели загружать все данные в память,
> поэтому нормально часть логики вынести в эти сервисы, которые являются адаптерами к внешнему миру.
> В данном случае, `ISessionRepository` может кинуть `SessionNotFoundException`, если нужного сеанса не нашлось.
> Согласитесь, это лучше, чем загружать всю БД в память, верно?

# Декораторы

Объекты в доменном проекте не имеют лишних, внешних зависимостей: никакого логирования, трейсинга, аудита, сбора метрик и т.д.

Но подключать эти зависимости к доменному проекту нельзя, вместо них мы можем использовать декораторы.
Алгоритм выглядит так:
1. Выделить места, где необходимо добавить функциональность;
2. При необходимости выделить отдельные объекты для них;
3. Вынести эту функциональность в интерфейсы;
4. Вместо самостоятельного создания этих объектов и использования конкретных классов - внедрение зависимостей выделенных интерфейсов.

Для примера, добавим сбор метрик приложения:
- Количество купленных мест
- Количество забронированных мест

TODO: ссылка на этот проект

```csharp
public static class MetricsRegistry
{
    public static readonly Meter AppMeter = new Meter("CinemaBooking", "1.0.0");
    public static readonly Counter<long> BoughtSeatsCount = AppMeter.CreateCounter<long>(
        name: "seats-bought-count",
        unit: null,
        description: "Количество купленных мест");

    public static readonly Counter<long> BookedSeatsCount = AppMeter.CreateCounter<long>(
        name: "booked-seats-count",
        unit: null,
        description: "Количество забронированных мест"); 
}

public class MetricScrapperSeatService: ISeatService
{
    private readonly ISeatService _service;

    public MetricScrapperSeatService(ISeatService service)
    {
        _service = service;
    }

    public async Task<BookedSeat> BookSeatAsync(int sessionId, int place, int clientId, CancellationToken token = default)
    {
        var booked = await _service.BookSeatAsync(sessionId, place, clientId, token);
        MetricsRegistry.BookedSeatsCount.Add(1);
        return booked;
    }

    public async Task<BoughtSeat> BuySeatAsync(int sessionId, int place, int clientId, CancellationToken token = default)
    {
        var bought = await _service.BuySeatAsync(sessionId, place, clientId, token);
        MetricsRegistry.BoughtSeatsCount.Add(1);
        return bought;
    }
}
```

# Можно разные эндпоинты подключать быстро

Для примера, мы использовали только HTTP Rest интерфейс. Но не вебом едины. Есть и другие интерфейсы входа.

## gRPC интерфейс

Сперва, попробуем добавить gRPC интерфейс.
Для этого добавим отдельный проект, в котором реализуем всю нужную функциональность.

Вначале, сам proto файл:
```protobuf
syntax = 'proto3';

option csharp_namespace = "CinemaBooking.Grpc";

enum OperationResultCode {
   Ok = 0;
   SessionNotFound = 1;
   SeatNotFound = 2;
   SeatBooked = 3;
   SeatBought = 4;
}

message BookRequest {
   int32 sessionId = 1;
   int32 seatNumber = 2;
   int32 userId = 3;
}

message BookResponse {
   OperationResultCode resultCode = 1;
}

message BuyRequest {
   int32 sessionId = 1;
   int32 seatNumber = 2;
   int32 userId = 3;
}

message BuyResponse {
   OperationResultCode resultCode = 1;
}

service SeatService {
   rpc BookSeat(BookRequest) returns (BookResponse);
   rpc BuySeat(BuyRequest) returns (BuyResponse);
}
```

А теперь сгенерируем все нужные классы и реализуем наш сервис.

```csharp
public class GrpcSeatService: SeatService.SeatServiceBase
{
    private readonly ISeatService _service;

    public GrpcSeatService(ISeatService service)
    {
        _service = service;
    }
    
    public override async Task<BookResponse> BookSeat(BookRequest request, ServerCallContext context)
    {
        var code = await ExecuteGetResultCodeAsync(t => _service.BookSeatAsync(request.SessionId, request.SeatNumber, request.UserId, t), context.CancellationToken);
        return new BookResponse() {ResultCode = code};
    }

    public override async Task<BuyResponse> BuySeat(BuyRequest request, ServerCallContext context)
    {
        var code = await ExecuteGetResultCodeAsync(t => _service.BuySeatAsync(request.SessionId, request.SeatNumber, request.UserId, t), context.CancellationToken);
        return new BuyResponse() {ResultCode = code};
    }

    private static async Task<OperationResultCode> ExecuteGetResultCodeAsync(Func<CancellationToken, Task> code, CancellationToken token)
    {
        try
        {
            await code(token);
            return OperationResultCode.Ok;
        }
        catch (SessionNotFoundException)
        {
            return OperationResultCode.SessionNotFound;
        }
        catch (SeatNotFoundException)
        {
            return OperationResultCode.SeatNotFound;
        }
        catch (SeatBoughtException)
        {
            return OperationResultCode.SeatBought;
        }
        catch (SeatBookedException)
        {
            return OperationResultCode.SeatBought;
        }
    }
}
```

Вот gRPC и добавили - без лишней мороки.

## Консольное приложение

А теперь попробуем что-нибудь посложнее.
Представим, что мы захотели автоматизировать работу и нужно создать консольное приложение.
Тут тоже ничего сложного не будет: все что нам нужно - доменная сборка и реализация сервисов. 
Всего 2 зависимости.

```csharp
// Main
var arguments = CommandLineArguments.FromCommandLineArguments(args);

await using var database = GetDatabaseConnection();
var repo = new PostgresSessionRepository(database);
var seatService = new SeatService(repo);
var (command, sessionId, seat, clientId) = arguments;

var responseCode = 0;
try
{
    switch (command)
    {
        case OperationType.Book:
            try
            {
                await seatService.BookSeatAsync(sessionId, seat, clientId);
                Console.WriteLine($"Место забронировано");
            }
            catch (SeatBookedException e) when (e.ClientId == clientId)
            {
                Console.WriteLine($"Вы уже забронировали это место");
            }

            break;
        case OperationType.Buy:
            try
            {
                await seatService.BuySeatAsync(sessionId, seat, clientId);
                Console.WriteLine($"Место куплено");
            }
            catch (SeatBoughtException e) when (e.ClientId == clientId)
            {
                Console.WriteLine($"Вы уже купили это место");
            }

            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(command), command, "Неизвестная команда");
    }
}
catch (SeatNotFoundException snf)
{
    Console.WriteLine($"Место {snf.Seat} не найдено");
    responseCode = 2;
}
catch (SessionNotFoundException snf)
{
    Console.WriteLine($"Сеанс {snf.SessionId} не найден");
    responseCode = 3;
}
catch (SeatBookedException)
{
    Console.WriteLine($"Указанное место забронировано за другим посетителем");
    responseCode = 4;
}
catch (SeatBoughtException)
{
    Console.WriteLine($"Указанное место куплено другим посетителем");
    responseCode = 5;
}

return responseCode;
```

В итоге, для создания простого консольного приложения потребовалось добавить только 3 файла.

Тот же самый трюк можно провернуть и для других точек входа:
- Оконное приложение
- Мобильное приложение
- Serverless

TODO: добавить ссылку на консольный проект

# Не люблю Repository\<T> (или "Немного про IRepository")

Хотелось бы побольше поговорить про `IRepository`, а если быть точнее, то про его обобщенный интерфейс, который выглядит примерно так:

```csharp
interface IRepository<T>
{
    void Add(T item);
    void Remove(T item);
    T GetById(int id);
    IEnumerable<T> GetAll();
}
```

А дальше начинается целый цирк:
```csharp
interface IUserRepository: IRepository<User>
{ }
```

И вот дальше начинается ад:
- Мы указали, тип ID для этого `T` - `int`, но что если это не `int`, а какой-нибудь `Guid`?
- А если мне не нужны какие-то методы? Зачем тащить ненужные?
- Какой тип мне использовать: `IRepository<User>` или `IUserRepository`? 
- Если я в этот базовый `IRepository` добавлю единственный метод, то все наследники должны реализовать указанный, т.е. мы жестко связываем все объекты.

Я предпочитаю специализированные сервисы `IXRepository`, в которых содержатся только необходимые методы.
Плюсы этого подхода:
- Все методы интерфейса необходимы - не нужно писать лишний код;
- Работу каждого метода можно оптимизировать - можно писать логику под конкретное использование (например, оптимизированный SQL запрос).

Хорошо. Решили, что будем создавать `IRepository` под конкретные варианты использования. Остался вопрос - что делать с запросами на чтение? Например, мы хотим получить все сеансы за эту неделю, или статистику посещения кинотеатра за весь месяц.

Есть 2+ вариантов:
1. Под каждый такой вариант писать отдельный метод репозитория. Например,
   ```csharp
   interface ISessionRepository
   {
       int GetTotalSessionsCountForLastWeek();
       int GetMostPopularSeats();
       // ...
   }
   ```
   Этот подход мне не нравится, т.к. в бизнес-логике появляется слишком много ненужных методов.
   И это затрудняет понимание кода.

2. Использовать подключение к источнику данных напрямую - делать прямые запросы к БД, другим сервисам. 
   Для примера, я добавил контроллер для получения информации из БД, используя этот подход. 
   ```csharp
   [ApiController]
   [Route("admin")]
   public class AdminController: ControllerBase
   {
       private readonly SessionDbContext _context;
   
       public AdminController(SessionDbContext context)
       {
           _context = context;
       }
   
       [HttpGet("sessions")]
       public async Task<IActionResult> GetAllSessionsAsync(CancellationToken token)
       {
           return Ok(await _context.Sessions.Select(s => new
           {
               s.Id, s.Start, s.End, s.MovieId
           }).ToListAsync(token));
       }
   }   
   ```

<spoiler title="CQRS">

Второй подход называют CQRS: когда все запросы разделяются на чтение и запись.

В данном примере, для чтения мы напрямую работаем с БД, а для записи - используем операции над доменными объектами.

Плюсы данного подхода:
- Для чтения можно использовать различные источники, а не только БД. Например, мы могли бы использовать Redis для кэширования сложных запросов,
  и работать с моделями БД напрямую, а не через доменные;
- Производительность операций чтения увеличивается, т.к. не нужно тратить время на маппинг и дополнительные проверки;
- Модифицирующие операции все так же защищены, т.к. работаем мы с доменными объектами;

Что касается синхронизации со схемой в БД, то мы используем ORM. Если схема будет изменена (и при этом будет нарушен запрос чтения), то проект даже не скомпилируется.

</spoiler>

3. По середине между 1 и 2 пунктом лежит 3-ий вариант - паттерн "Спецфикация". 
   Его суть заключается в том, что для этого репозитория мы добавляем отдельный метод, именуемый примерно как `GetAll(Specification spec)`.
   То, как этот класс `Spcification` реализован зависит от разработчика, но она должна позволять фильтровать элементы. Для .NET чаще всего видел решение через использование `Expression`. 
   Сам паттерн в базовом представлении выглядит следующим образом:
```csharp
public abstract class Specification<T>
{
    public abstract Expression<Func<T, bool>> Expression { get; }

    public Specification<T> And(Specification<T> other)
    {
        return new AndSpecification<T>(this, other);
    }

    public Specification<T> Or(Specification<T> other)
    {
        return new OrSpecification<T>(this, other);
    }

    public Specification<T> Not()
    {
        return new NotSpecification<T>(this);
    }

    public static implicit operator Expression<Func<T, bool>>(Specification<T> spec) => spec.Expression;
}
```
   А теперь реализуем свои спецификации:
   ```csharp
   public static class SessionSpecifications
   {
       public static Specification<DatabaseSession> LastDays(int days)
       {
           var boundary = DateTime.SpecifyKind( DateTime.Now - TimeSpan.FromDays(days), DateTimeKind.Utc);
           return new GenericSpecification<DatabaseSession>(session => boundary < session.Start);
       }
   
       public static Specification<DatabaseSession> AllFreeSeats()
       {
           return new GenericSpecification<DatabaseSession>(session =>
               session.Seats.All(seat => seat.Type == SeatType.Free));
       }
   }
   
   [HttpGet("sessions/unvisited")]
   public async Task<IActionResult> GetUnvisitedSessions([FromQuery][Required] int days, CancellationToken token)
   {
       var response = await _context.Sessions
                                    .Where(SessionSpecifications.LastDays(days).And(SessionSpecifications.AllFreeSeats()))
                                    .ToListAsync(token);
       return Ok(response);
   }
   ```
   
   Я не стал реализовывать `GetAll(Specification<Session> specification)` в `ISessionRepository`, чтобы не нагромождать место лишним кодом.   

# Заключение

Чистая архитектура - это идея, а не готовый фреймворк. Идея разделения бизнес-логики от внешнего мира. 

Реализовать ее можно самыми различными способами. Например:
- Вместо исключений возвращать специализированные `Result<T>`;
- Использовать абстрактные методы для `Seat` вместо паттерна посетитель;
- Вынести логику напрямую в объекты и не вводить отдельный сервис `SeatService` (текущая логика довольно тривиальна).

Если сильно упороться в последний пункт, то мы уйдем в DDD, но это уже другая история.
