namespace CinemaBooking.Domain.Exceptions;

public class SeatBookedException: DomainException
{
    /// <summary>
    /// Клиент, который забронировал место
    /// </summary>
    public int ClientId { get; }

    public override string Message => $"Клиент с Id {ClientId} не найден";

    public SeatBookedException(int clientId)
    {
        ClientId = clientId;
    }
}