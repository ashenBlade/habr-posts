using CinemaBooking.Domain.Exceptions;

namespace CinemaBooking.Domain;

public abstract class Seat: IEquatable<Seat>
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

    /// <summary>
    /// Забронировать место за указанным клиентом
    /// </summary>
    /// <param name="clientId">Id клиента, за которым нужно забронировать место</param>
    /// <returns>Забронированное место</returns>
    /// <exception cref="SeatBoughtException">Указанное место уже куплено, возможно этим же посетителем</exception>
    /// <exception cref="SeatBookedException">Указанное место забронировано, возможно этим же посетителем</exception>
    public abstract BookedSeat Book(int clientId);
    
    /// <summary>
    /// Купить место для указанного посетителя
    /// </summary>
    /// <param name="clientId">Id клиента, для которого нужно купить место</param>
    /// <returns>Купленное место</returns>
    /// <exception cref="SeatBoughtException">Указанное место уже куплено, возможно этим же посетителем</exception>
    /// <exception cref="SeatBookedException">Указанное место забронировано другим посетителем</exception>
    public abstract BoughtSeat Buy(int clientId);

    public abstract T Accept<T>(ISeatVisitor<T> visitor);
    
    public abstract bool Equals(Seat? other);
    public abstract override bool Equals(object? other);
    public abstract override int GetHashCode();

    public static bool operator ==(Seat? left, Seat? right)
    {
        if (left is null)
        {
            return right is null;
        }

        return left.Equals(right);
    }

    public static bool operator !=(Seat? left, Seat? right)
    {
        return !( left == right );
    }
}