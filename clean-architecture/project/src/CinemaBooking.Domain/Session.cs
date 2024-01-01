using CinemaBooking.Domain.Exceptions;

namespace CinemaBooking.Domain;

public class Session
{
    public Session(int id, SessionInterval interval, int movieId, IEnumerable<Seat> seats)
    {
        ArgumentNullException.ThrowIfNull(interval);
        ArgumentNullException.ThrowIfNull(seats);

        Id = id;
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

    /// <summary>
    /// Идентификатор сеанса
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// Промежуток времени, занимаемый сеансом.
    /// Включает как время показа самого фильма, так и время обслуживания между сеансами
    /// </summary>
    public SessionInterval Interval { get; }

    /// <summary>
    /// Фильм, который показывается на этом сеансе
    /// </summary>
    public int MovieId { get; }

    private readonly Seat[] _seats;

    /// <summary>
    /// Места, которые принадлежат этому сеансу
    /// </summary>
    public IReadOnlyCollection<Seat> Seats => _seats;

    /// <summary>
    /// Купить указанное место для посетителя
    /// </summary>
    /// <param name="place">Место, которое нужно купить</param>
    /// <param name="clientId">Клиент, которому нужно купить место</param>
    /// <returns><c>true</c> - место было куплено, <c>false</c> - место УЖЕ было куплено этим клиентом</returns>
    /// <exception cref="SeatNotFoundException">Место с указанным номером не найдено</exception>
    /// <exception cref="SeatBoughtException">Указанное место куплено другим посетителем</exception>
    /// <exception cref="SeatBookedException">Указанное место забронировано другим посетителем</exception>
    public BoughtSeat Buy(int place, int clientId)
    {
        var (seat, index) = FindSeatByPlace(place);

        var visitor = new BuyingSeatVisitor(clientId, index, this);

        return seat.Accept(visitor);
    }

    /// <summary>
    /// Найти место по указанному номеру
    /// </summary>
    /// <param name="place">Номер места</param>
    /// <returns>Найденное место и индекс в массиве</returns>
    private (Seat Seat, int Index) FindSeatByPlace(int place)
    {
        for (var i = 0; i < _seats.Length; i++)
        {
            if (_seats[i].Number == place)
            {
                return ( _seats[i], i );
            }
        }

        throw new SeatNotFoundException(place);
    }
    
    /// <summary>
    /// Посетитель, который покупает место для указанного клиента
    /// </summary>
    private class BuyingSeatVisitor : ISeatVisitor<BoughtSeat>
    {
        /// <summary>
        /// Клиент, на которого нужно оформить место
        /// </summary>
        public int ClientId { get; }

        /// <summary>
        /// Индекс в массиве мест сеанса <see cref="Session._seats"/>
        /// </summary>
        public int Index { get; }
        
        /// <summary>
        /// Сеанс, который мы обслуживаем
        /// </summary>
        public Session Parent { get; }

        public BuyingSeatVisitor(int clientId, int index, Session parent)
        {
            ClientId = clientId;
            Index = index;
            Parent = parent;
        }
        
        public BoughtSeat Visit(FreeSeat freeSeat)
        {
            var seat = new BoughtSeat(freeSeat.Number, ClientId);
            Parent._seats[Index] = seat;
            return seat;
        }

        public BoughtSeat Visit(BoughtSeat boughtSeat)
        {
            throw new SeatBoughtException(boughtSeat.ClientId);
        }

        public BoughtSeat Visit(BookedSeat bookedSeat)
        {
            if (bookedSeat.ClientId == ClientId)
            {
                var boughtSeat = new BoughtSeat(bookedSeat.Number, ClientId);
                Parent._seats[Index] = boughtSeat;
                return boughtSeat;
            }

            throw new SeatBookedException(bookedSeat.ClientId);
        }
    }

    /// <summary>
    /// Забронировать место за указанным клиентом 
    /// </summary>
    /// <param name="place">Место, которое нужно забронировать</param>
    /// <param name="clientId">Клиент, за которым нужно забронировать место</param>
    /// <returns><c>true</c> - место забронировано, <c>false</c> - место уже было забронировано этим клиентом</returns>
    /// <exception cref="SeatNotFoundException">Место с указанным номером не найдено</exception>
    /// <exception cref="SeatBoughtException">Указанное место уже куплено, возможно, этим же самым посетителем</exception>
    /// <exception cref="SeatBookedException">Указанное место забронировано другим посетителем</exception>
    public BookedSeat Book(int place, int clientId)
    {
        var (seat, index) = FindSeatByPlace(place);
        var visitor = new BookingSeatVisitor(this, index, clientId);
        return seat.Accept(visitor);
    }

    /// <summary>
    /// Реализация <see cref="ISeatVisitor{T}"/>, который бронирует место.
    /// Возвращает:
    /// - Новый объект, если место было забронировано,
    /// - null - если место уже было забронировано этим же посетителем ранее
    /// </summary>
    private class BookingSeatVisitor : ISeatVisitor<BookedSeat>
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
        
        public BookedSeat Visit(FreeSeat freeSeat)
        {
            var bookedSeat = new BookedSeat(freeSeat.Number, ClientId);
            Parent._seats[Index] = bookedSeat;
            return bookedSeat;
        }

        public BookedSeat Visit(BoughtSeat boughtSeat)
        {
            throw new SeatBoughtException(boughtSeat.ClientId);
        }

        public BookedSeat Visit(BookedSeat bookedSeat)
        {
            throw new SeatBookedException(bookedSeat.ClientId);
        }
    }
}
