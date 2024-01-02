namespace CinemaBooking.Domain;

public class FreeSeat: Seat
{
    public FreeSeat(int number) : base(number)
    { }

    public override BookedSeat Book(int clientId)
    {
        return new BookedSeat(Number, clientId);
    }

    public override BoughtSeat Buy(int clientId)
    {
        return new BoughtSeat(Number, clientId);
    }

    public override bool Equals(Seat? other)
    {
        if (other is null)
        {
            return false;
        }

        return other is FreeSeat free && free.Number == Number;
    }

    public override bool Equals(object? other)
    {
        return other is Seat seat && Equals(seat);
    }

    public override int GetHashCode()
    {
        return Number.GetHashCode();
    }

    public override T Accept<T>(ISeatVisitor<T> visitor)
    {
        return visitor.Visit(this);
    }
}