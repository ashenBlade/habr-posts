using System.Dynamic;

namespace CinemaBooking.Domain.Exceptions;

public class SeatNotFoundException: DomainException
{
    public int Seat { get; }
    public override string Message => $"Место под номером {Seat} не найдено";

    public SeatNotFoundException(int seat)
    {
        Seat = seat;
    }
}