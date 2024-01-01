using CinemaBooking.Domain;

namespace CinemaBooking.Services.SessionRepository;

public class DatabaseSeatMapperSeatVisitor: ISeatVisitor<DatabaseSeat>
{
    private readonly int _sessionId;

    public DatabaseSeatMapperSeatVisitor(int sessionId)
    {
        _sessionId = sessionId;
    }
    
    public DatabaseSeat Visit(FreeSeat freeSeat)
    {
        return new DatabaseSeat()
        {
            Type = SeatType.Free, Number = freeSeat.Number, ClientId = null, SessionId = _sessionId,
        };
    }

    public DatabaseSeat Visit(BoughtSeat boughtSeat)
    {
        return new DatabaseSeat()
        {
            SessionId = _sessionId, Type = SeatType.Bought, Number = boughtSeat.Number, ClientId = boughtSeat.ClientId,
        };
    }

    public DatabaseSeat Visit(BookedSeat bookedSeat)
    {
        return new DatabaseSeat()
        {
            Type = SeatType.Booked, Number = bookedSeat.Number, SessionId = _sessionId, ClientId = bookedSeat.ClientId,
        };
    }
}