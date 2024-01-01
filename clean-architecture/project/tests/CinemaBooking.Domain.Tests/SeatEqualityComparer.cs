namespace CinemaBooking.Domain.Tests;

public class SeatEqualityComparer: IEqualityComparer<Seat>
{
    public bool Equals(Seat? x, Seat? y)
    {
        if (x is null || y is null)
        {
            return false;
        }

        return Check(( dynamic ) x, ( dynamic ) y); 
    }

    public int GetHashCode(Seat obj)
    {
        return obj.Number;
    }

    private bool Check(FreeSeat first, FreeSeat second) => first.Number == second.Number;

    private bool Check(BookedSeat first, BookedSeat second) =>
        first.Number == second.Number && 
        first.ClientId == second.ClientId;
    
    private bool Check(BoughtSeat first, BoughtSeat second) =>
        first.Number == second.Number && 
        first.ClientId == second.ClientId; 
}