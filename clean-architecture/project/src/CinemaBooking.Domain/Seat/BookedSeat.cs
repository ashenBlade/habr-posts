using CinemaBooking.Domain.Exceptions;

namespace CinemaBooking.Domain;

public class BookedSeat: Seat
{
    /// <summary>
    /// Клиент, который забронировал место
    /// </summary>
    public int ClientId { get; }

    public BookedSeat(int number, int clientId) : base(number)
    {
        ClientId = clientId;
    }

    public override BookedSeat Book(int clientId)
    {
        if (ClientId == clientId)
        {
            return this;
        }
        
        throw new SeatBookedException(ClientId);
    }

    public override BoughtSeat Buy(int clientId)
    {
        if (ClientId == clientId)
        {
            return new BoughtSeat(Number, clientId);
        }

        throw new SeatBookedException(ClientId);
    }

    public override bool Equals(Seat? other)
    {
        return other is BookedSeat booked && 
               booked.Number == Number &&
               booked.ClientId == ClientId;
    }

    public override bool Equals(object? other)
    {
        return other is Seat seat && Equals(seat);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(2, Number, ClientId);
    }
    
    public override T Accept<T>(ISeatVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}