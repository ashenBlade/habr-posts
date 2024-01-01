using CinemaBooking.Domain.Exceptions;

namespace CinemaBooking.Domain.Tests;

public class StubSessionRepository: ISessionRepository
{
    private readonly Session[] _sessions;

    public StubSessionRepository(IEnumerable<Session> sessions)
    {
        _sessions = sessions.ToArray();
    }
    public Task<Session> GetSessionByIdAsync(int sessionId, CancellationToken token = default)
    {
        var found = _sessions.FirstOrDefault(s => s.Id == sessionId);
        return found is null 
                   ? Task.FromException<Session>(new SessionNotFoundException(sessionId)) 
                   : Task.FromResult(found);
    }

    public async Task UpdateSeatAsync(int sessionId, Seat seat, CancellationToken token = default)
    {
        
    }
}