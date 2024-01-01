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
        return new Session(interval, found.MovieId, seats);
    }
    
    public async Task UpdateSeatAsync(int sessionId, Seat seat, CancellationToken token = default)
    {
        var found = await _context.Sessions
                                  .Include(s => s.Seats)
                                  .FirstOrDefaultAsync(s => s.Id == sessionId, token);
        if (found is null)
        {
            throw new SessionNotFoundException(sessionId);
        }

        var databaseSeat = seat.Accept(new DatabaseSeatMapperSeatVisitor(sessionId));
        
        var existingSeat = found.Seats.FirstOrDefault(s => s.Number == seat.Number);
        if (existingSeat is null)
        {
            found.Seats.Add(databaseSeat);
        }
        else
        {
            existingSeat.Type = databaseSeat.Type;
            existingSeat.ClientId = databaseSeat.ClientId;
            existingSeat.SessionId = databaseSeat.SessionId;
            existingSeat.Number = databaseSeat.Number;
        }

        await _context.SaveChangesAsync(token);
    }
}