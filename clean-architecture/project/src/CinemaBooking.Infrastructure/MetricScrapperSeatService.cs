using CinemaBooking.Domain;

namespace CinemaBooking.Infrastructure;

public class MetricScrapperSeatService: ISeatService
{
    private readonly ISeatService _service;

    public MetricScrapperSeatService(ISeatService service)
    {
        _service = service;
    }

    public async Task<BookedSeat> BookSeatAsync(int sessionId, int place, int clientId, CancellationToken token = default)
    {
        var booked = await _service.BookSeatAsync(sessionId, place, clientId, token);
        MetricsRegistry.BookedSeatsCount.Add(1);
        return booked;
    }

    public async Task<BoughtSeat> BuySeatAsync(int sessionId, int place, int clientId, CancellationToken token = default)
    {
        var bought = await _service.BuySeatAsync(sessionId, place, clientId, token);
        MetricsRegistry.BoughtSeatsCount.Add(1);
        return bought;
    }
}