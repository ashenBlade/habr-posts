namespace CinemaBooking.Domain;

public class BookedSeat: Seat
{
    public int ClientId { get; }

    public BookedSeat(int number, int clientId) : base(number)
    {
        ClientId = clientId;
    }

    public override T Accept<T>(ISeatVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}