namespace CinemaBooking.WebApi.Dto;

public class BookSeatRequestDto
{
    public int SessionId { get; set; }
    public int Place { get; set; }
    public int ClientId { get; set; }
}