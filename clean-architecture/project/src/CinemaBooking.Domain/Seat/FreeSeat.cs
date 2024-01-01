namespace CinemaBooking.Domain;

public class FreeSeat: Seat
{
    public FreeSeat(int number) : base(number)
    { }

    public override T Accept<T>(ISeatVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}