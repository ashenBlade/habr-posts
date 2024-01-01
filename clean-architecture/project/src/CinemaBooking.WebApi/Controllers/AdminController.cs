using CinemaBooking.Services.SessionRepository;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.WebApi.Controllers;

[ApiController]
[Route("admin")]
public class AdminController: ControllerBase
{
    private readonly SessionDbContext _context;

    public AdminController(SessionDbContext context)
    {
        _context = context;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetAllSessionsAsync(CancellationToken token)
    {
        return Ok(await _context.Sessions.Select(s => new
        {
            s.Id, s.Start, s.End, s.MovieId
        }).ToListAsync(token));
    }

    
    [HttpGet("sessions/{sessionId:int}")]
    public async Task<IActionResult> GetSeatsForSessionAsync(int sessionId, CancellationToken token)
    {
        try
        {
            return Ok(await _context.Sessions
                                    .AsNoTracking()
                                    .Include(s => s.Seats)
                                    .Select(s => new
                                     {
                                         s.Id,
                                         s.Start,
                                         s.End,
                                         s.MovieId,
                                         Seats = s.Seats.Select(seat => new
                                         {
                                             Type = seat.Type.ToString(),
                                             seat.Number,
                                             seat.ClientId
                                         })
                                     })
                                    .SingleAsync(s => s.Id == sessionId, token));
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}