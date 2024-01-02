using CinemaBooking.Domain.Exceptions;

namespace CinemaBooking.Domain;

public class Session
{
    public Session(int id, IEnumerable<Seat> seats)
    {
        Id = id;
        _seats = BuildSeatsArray(seats);
    }

    private static Seat[] BuildSeatsArray(IEnumerable<Seat> seats)
    {
        ArgumentNullException.ThrowIfNull(seats);
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

    private readonly Seat[] _seats;

    /// <summary>
    /// Места, которые принадлежат этому сеансу
    /// </summary>
    public IReadOnlyCollection<Seat> Seats => _seats;

    /// <summary>
    /// Купить указанное место для клиента
    /// </summary>
    /// <param name="place">Место, которое нужно купить</param>
    /// <param name="clientId">Клиент, которому нужно купить место</param>
    /// <returns><c>true</c> - место было куплено, <c>false</c> - место УЖЕ было куплено этим клиентом</returns>
    /// <exception cref="SeatNotFoundException">Место с указанным номером не найдено</exception>
    /// <exception cref="SeatBoughtException">Указанное место куплено, возможно этим самым клиентом</exception>
    /// <exception cref="SeatBookedException">Указанное место забронировано другим клиентом</exception>
    public BoughtSeat Buy(int place, int clientId)
    {
        var (seat, index) = FindSeatByPlace(place);

        var bought = seat.Buy(clientId);
        if (bought == seat)
        {
            throw new SeatBoughtException(clientId);
        }

        _seats[index] = seat;
        return bought;
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
    /// Забронировать место за указанным клиентом 
    /// </summary>
    /// <param name="place">Место, которое нужно забронировать</param>
    /// <param name="clientId">Клиент, за которым нужно забронировать место</param>
    /// <returns><c>true</c> - место забронировано, <c>false</c> - место уже было забронировано этим клиентом</returns>
    /// <exception cref="SeatNotFoundException">Место с указанным номером не найдено</exception>
    /// <exception cref="SeatBoughtException">Указанное место уже куплено, возможно этим же самым посетителем</exception>
    /// <exception cref="SeatBookedException">Указанное место забронировано, возможно этим самым посетителем посетителем</exception>
    public BookedSeat Book(int place, int clientId)
    {
        var (seat, index) = FindSeatByPlace(place);
        var booked = seat.Book(clientId);
        if (booked == seat)
        {
            throw new SeatBookedException(clientId);
        }
        
        _seats[index] = booked;
        return booked;
    }
}