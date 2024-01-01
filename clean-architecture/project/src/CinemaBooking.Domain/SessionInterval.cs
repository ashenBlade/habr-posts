namespace CinemaBooking.Domain;

public class SessionInterval
{
    public SessionInterval(DateTime start, DateTime end)
    {
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(start), start,
                "Время начала сеанса не может быть больше времени окончания");
        }
        
        Start = start;
        End = end;
    }

    public DateTime Start { get; }
    public DateTime End { get; }
}