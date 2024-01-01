using CinemaBooking.Domain.Exceptions;

namespace CinemaBooking.Domain;

/// <summary>
/// Объект доступа к данным сеансов и мест
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// Найти сеанс в кинотеатре по переданному Id
    /// </summary>
    /// <param name="sessionId">Id сеанса</param>
    /// <param name="token">Токен отмены</param>
    /// <returns>Найденный сеанс</returns>
    /// <exception cref="SessionNotFoundException">Сеанс с указанным Id не найден</exception>
    public Task<Session> GetSessionByIdAsync(int sessionId, CancellationToken token = default);

    /// <summary>
    /// Обновить указанное место
    /// </summary>
    /// <param name="sessionId">Сессия, для которой нужно обновить место</param>
    /// <param name="seat">Место, которое нужно выставить</param>
    /// <param name="token">Токен отмены</param>
    public Task UpdateSeatAsync(int sessionId, Seat seat, CancellationToken token = default);
}