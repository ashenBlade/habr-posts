namespace CinemaBooking.Domain;

public interface ISeatVisitor<out T>
{
    public T Visit(FreeSeat seat);
    public T Visit(BookedSeat seat);
    public T Visit(BoughtSeat seat);
}