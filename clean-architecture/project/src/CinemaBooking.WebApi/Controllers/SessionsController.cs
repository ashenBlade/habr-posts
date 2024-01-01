using System.ComponentModel.DataAnnotations;
using CinemaBooking.Domain;
using CinemaBooking.WebApi.Filters;
using Microsoft.AspNetCore.Mvc;

namespace CinemaBooking.WebApi.Controllers;

[ApiController]
[Route("sessions")]
[DomainExceptionFilter]
public class SessionsController: ControllerBase
{
    private readonly IBookingService _bookingService;

    public SessionsController(IBookingService bookingService)
    {
        _bookingService = bookingService;
    }

    [HttpPut("{sessionId:int}/places/{placeId:int}/book")]
    public async Task<IActionResult> BookSeat(int sessionId, int placeId, [FromQuery][Required] int userId, CancellationToken token = default)
    {
        await _bookingService.BookSeatAsync(sessionId, placeId, userId, token);
        return Ok();
    }
    
    [HttpPut("{sessionId:int}/places/{placeId:int}/buy")]
    public async Task<IActionResult> BuySeat(int sessionId, int placeId, [FromQuery][Required] int userId, CancellationToken token = default)
    {
        await _bookingService.BuySeatAsync(sessionId, placeId, userId, token);
        return Ok();
    }
}