using CinemaBooking.Domain;
using CinemaBooking.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Services.SessionRepository;

public class PostgresSessionRepository: ISessionRepository
{
    private readonly SessionDbContext _context;

    public PostgresSessionRepository(SessionDbContext context)
    {
        _context = context;
    }
    
    public async Task<Session> GetSessionByIdAsync(int sessionId, CancellationToken token = default)
    {
        var found = await _context.Sessions
                                  .AsNoTracking()
                                  .Include(s => s.Seats)
                                  .FirstOrDefaultAsync(s => s.Id == sessionId, token);
        if (found is null)
        {
            throw new SessionNotFoundException(sessionId);
        }

        var interval = new SessionInterval(found.Start, found.End);
        var seats = found.Seats.Select(seat => seat.ToDomainSeat());
        return new Session(found.Id, interval, found.MovieId, seats);
    }
    
    public async Task UpdateSeatAsync(int sessionId, Seat seat, CancellationToken token = default)
    {
        var databaseSeat = seat.Accept(new DatabaseSeatMapperSeatVisitor(sessionId));
        var updated = await _context.Seats
                                    .Where(s => s.SessionId == sessionId && s.Number == seat.Number)
                                    .ExecuteUpdateAsync(calls => calls.SetProperty(s => s.ClientId, databaseSeat.ClientId)
                                                                      .SetProperty(s => s.Type, databaseSeat.Type), token);
        if (updated == 0)
        {
            throw new SessionNotFoundException(sessionId);
        }
    }
}