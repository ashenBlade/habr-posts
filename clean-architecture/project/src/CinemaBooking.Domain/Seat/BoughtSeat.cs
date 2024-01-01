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
    
    public override T Accept<T>(ISeatVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}