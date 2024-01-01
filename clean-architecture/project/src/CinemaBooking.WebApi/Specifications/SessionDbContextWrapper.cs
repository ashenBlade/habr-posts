using CinemaBooking.Services.SessionRepository;

namespace CinemaBooking.WebApi.Specifications;

public class SessionDbContextWrapper
{
    private readonly SessionDbContext _context;

    public SessionDbContextWrapper(SessionDbContext context)
    {
        _context = context;
    }
    
    
}