namespace CinemaBooking.Domain;

public class SeatService: ISeatService
{
    private readonly ISessionRepository _sessionRepository;

    public SeatService(ISessionRepository sessionRepository)
    {
        ArgumentNullException.ThrowIfNull(sessionRepository);
        _sessionRepository = sessionRepository;
    }
    
    public async Task<BookedSeat> BookSeatAsync(int sessionId, int place, int clientId, CancellationToken token = default)
    {
        var session = await _sessionRepository.GetSessionByIdAsync(sessionId, token);
        var bookedSeat = session.Book(place, clientId);
        await _sessionRepository.UpdateSeatAsync(sessionId, bookedSeat, token);
        return bookedSeat;
    }

    public async Task<BoughtSeat> BuySeatAsync(int sessionId, int place, int clientId, CancellationToken token = default)
    {
        var session = await _sessionRepository.GetSessionByIdAsync(sessionId, token);
        var boughtSeat = session.Buy(place, clientId);
        await _sessionRepository.UpdateSeatAsync(sessionId, boughtSeat, token);
        return boughtSeat;
    }
}