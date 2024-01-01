namespace CinemaBooking.Domain.Exceptions;

public class SeatBoughtException: DomainException
{
    /// <summary>
    /// Клиент, на которого уже было куплено место
    /// </summary>
    public int ClientId { get; }

    public override string Message => $"Указанное место уже куплено клиентом {ClientId}";

    public SeatBoughtException(int clientId)
    {
        ClientId = clientId;
    }
}