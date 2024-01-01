namespace CinemaBooking.Services.SessionRepository;

public class DatabaseSession
{
    public int Id { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public int MovieId { get; set; }
    public ICollection<DatabaseSeat> Seats { get; set; } 
}