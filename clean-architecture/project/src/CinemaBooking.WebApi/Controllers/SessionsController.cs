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
    private readonly ISeatService _seatService;

    public SessionsController(ISeatService seatService)
    {
        _seatService = seatService;
    }

    [HttpPut("{sessionId:int}/places/{placeId:int}/book")]
    public async Task<IActionResult> BookSeat(int sessionId, int placeId, [FromQuery][Required] int userId, CancellationToken token = default)
    {
        await _seatService.BookSeatAsync(sessionId, placeId, userId, token);
        return Ok();
    }
    
    [HttpPut("{sessionId:int}/places/{placeId:int}/buy")]
    public async Task<IActionResult> BuySeat(int sessionId, int placeId, [FromQuery][Required] int userId, CancellationToken token = default)
    {
        await _seatService.BuySeatAsync(sessionId, placeId, userId, token);
        return Ok();
    }
}