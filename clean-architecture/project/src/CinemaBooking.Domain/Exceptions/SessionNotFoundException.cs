namespace CinemaBooking.Domain.Exceptions;

public class SessionNotFoundException: DomainException
{
    public int SessionId { get; }
    public override string Message => $"Сеанса {SessionId} не найдено";

    public SessionNotFoundException(int sessionId)
    {
        SessionId = sessionId;
    }
}