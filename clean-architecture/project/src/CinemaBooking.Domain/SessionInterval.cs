namespace CinemaBooking.Domain;

public class SessionInterval
{
    /// <summary>
    /// Время начала сеанса
    /// </summary>
    public DateTime Start { get; }
    
    /// <summary>
    /// Время окончания сеанса
    /// </summary>
    public DateTime End { get; }
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
}