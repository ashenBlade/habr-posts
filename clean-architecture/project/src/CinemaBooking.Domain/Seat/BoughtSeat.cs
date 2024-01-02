using CinemaBooking.Domain.Exceptions;

namespace CinemaBooking.Domain;

public class BoughtSeat: Seat
{
    /// <summary>
    /// Клиент, который купил место
    /// </summary>
    public int ClientId { get; }

    public BoughtSeat(int number, int clientId) : base(number)
    {
        ClientId = clientId;
    }

    public override BookedSeat Book(int clientId)
    {
        throw new SeatBoughtException(ClientId);
    }

    public override BoughtSeat Buy(int clientId)
    {
        if (ClientId == clientId)
        {
            return this;
        }
        
        throw new SeatBoughtException(ClientId);
    }

    public override bool Equals(Seat? other)
    {
        return other is BoughtSeat bought 
            && bought.Number == Number 
            && bought.ClientId == ClientId;
    }

    public override bool Equals(object? other)
    {
        return other is Seat seat && Equals(seat);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(1, Number, ClientId);
    }
    
    public override T Accept<T>(ISeatVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}