namespace CinemaBooking.Domain;

public interface ISeatVisitor<out T>
{
    T Visit(FreeSeat freeSeat);
    T Visit(BoughtSeat boughtSeat);
    T Visit(BookedSeat bookedSeat);
}