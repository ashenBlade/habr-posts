namespace CinemaBooking.Domain;

public class BookingService: IBookingService
{
    private readonly ISessionRepository _sessionRepository;

    public BookingService(ISessionRepository sessionRepository)
    {
        _sessionRepository = sessionRepository;
    }
    
    public async Task BookSeatAsync(int sessionId, int place, int clientId, CancellationToken token = default)
    {
        var session = await _sessionRepository.GetSessionByIdAsync(sessionId, token);
        if (session.TryBook(place, clientId, out var bookedSeat))
        { 
            await _sessionRepository.UpdateSeatAsync(sessionId, bookedSeat, token);
        }
    }

    public async Task BuySeatAsync(int sessionId, int place, int clientId, CancellationToken token = default)
    {
        var session = await _sessionRepository.GetSessionByIdAsync(sessionId, token);
        if (session.TryBuy(place, clientId, out var boughtSeat))
        {
            await _sessionRepository.UpdateSeatAsync(sessionId, boughtSeat, token);
        }
    }
}