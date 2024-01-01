namespace CinemaBooking.Domain;

public class BoughtSeat: Seat
{
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