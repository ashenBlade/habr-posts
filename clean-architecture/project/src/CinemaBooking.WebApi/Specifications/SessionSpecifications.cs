using CinemaBooking.Infrastructure.Specifications;
using CinemaBooking.Services.SessionRepository;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace CinemaBooking.WebApi.Specifications;

public static class SessionSpecifications
{
    public static Specification<DatabaseSession> LastDays(int days)
    {
        var boundary = DateTime.SpecifyKind( DateTime.Now - TimeSpan.FromDays(days), DateTimeKind.Utc);
        return new GenericSpecification<DatabaseSession>(session => boundary < session.Start);
    }

    public static Specification<DatabaseSession> AllFreeSeats()
    {
        return new GenericSpecification<DatabaseSession>(session =>
            session.Seats.All(seat => seat.Type == SeatType.Free));
    }
}