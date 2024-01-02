using CinemaBooking.Domain.Exceptions;
using Xunit;

namespace CinemaBooking.Domain.Tests;

public class SeatServiceTests
{
    private static readonly SeatEqualityComparer SeatComparer = new();
    
    [Fact]
    public async Task BookSeatAsync__WhenSeatIsFree__ShouldMarkSeatBooked()
    {
        var (sessionId, seatNumber, clientId) = ( 1, 1, 2 );
        var expectedSeat = new BookedSeat(seatNumber, clientId); 
        var session = new Session(sessionId, new[] {new FreeSeat(seatNumber)});
        var sessionRepo = new StubSessionRepository(new[] {session});
        var service = new SeatService(sessionRepo);

        var actual = await service.BookSeatAsync(sessionId, seatNumber, clientId);
        
        Assert.Equal(expectedSeat, actual, SeatComparer);
    }

    [Fact]
    public async Task BookSeatAsync__WhenSeatIsBought__ShouldThrowSeatBoughtException()
    {
        var (sessionId, seatNumber, clientId, boughtClientId) = ( 1, 1, 2, 10 );
        var session = new Session(sessionId, new[] {new BoughtSeat(seatNumber, boughtClientId)});
        var sessionRepo = new StubSessionRepository(new[] {session});
        var service = new SeatService(sessionRepo);

        await Assert.ThrowsAnyAsync<SeatBoughtException>(() => service.BookSeatAsync(sessionId, seatNumber, clientId));
    }

    [Fact]
    public async Task BookSeatAsync__WhenSeatIsBought__ShouldSpecifyCorrectClientIdInException()
    {
        var (sessionId, seatNumber, clientId, boughtClientId) = ( 1, 1, 2, 10 );
        var session = new Session(sessionId,  new[] {new BoughtSeat(seatNumber, boughtClientId)});
        var sessionRepo = new StubSessionRepository(new[] {session});
        var service = new SeatService(sessionRepo);

        var exception = (SeatBoughtException) ( await Record.ExceptionAsync(() => service.BookSeatAsync(sessionId, seatNumber, clientId)) )!;
        Assert.Equal(boughtClientId, exception.ClientId);
    }
}