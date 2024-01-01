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

Каждый создаст свою чистую архитектуру (и у каждого будет правильная)

Пример - бронирование мест в кинотеатре

Сказать что это разбиение horizontal slices, а есть еще  vertical slices + ссылка на статью какую-нибудь (для микросервисов сойдет)

---

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



# Тестирование

Как тестировать

> Все ниже можно в виде `Фичи/А зачем это все нужно` секции офомить с подпунктами

# Декораторы

Метрики, логирование, трейсинг

Выделяем интерфейсы даже там где не надо

# Не люблю Repository\<T>

Каждый делает базовый, который нужно переопределять

Лучше делать для каждого свой со своими методами

Если хочешь - спецификация (паттерн)

# Доменные исключения превращаются в Dto

Обрабатываем happy path, а исключения бизнес-логики превратить в Dto с помощью ExceptionFilter

# Можно разные эндпоинты подключать быстро

HTTP + gRPC изи добавлять

Под каждый Use Case свой метод - оптимально используем логику

# Сравнение в другими подходами

DDD (анемичные и богатые модели, отражение предметной области)

CQRS (медиатр) - можно и через него, но описать мой кейс когда было лишним

# Заключение


Документация?

