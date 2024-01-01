namespace CinemaBooking.Domain;

/// <summary>
/// Сервис для бронирования и покупки мест в кинотеатре
/// </summary>
public interface ISeatService
{
    /// <summary>
    /// Забронировать место для указанного сеанса
    /// </summary>
    /// <param name="sessionId">Сеанс, для которой нужно забронировать место</param>
    /// <param name="place">Место, которое нужно забронировать</param>
    /// <param name="clientId">Посетитель, на которого нужно создать бронь</param>
    /// <param name="token">Токен отмены</param>
    public Task BookSeatAsync(int sessionId, int place, int clientId, CancellationToken token = default);
    
    /// <summary>
    /// Купить место на сеанс для указанного посетителя 
    /// </summary>
    /// <param name="sessionId">Сеанс, на котором нужно купить место</param>
    /// <param name="place">Место, которое нужно купить</param>
    /// <param name="clientId">Посетитель, для которого нужно купить место</param>
    /// <param name="token">Токен отмены</param>
    public Task BuySeatAsync(int sessionId, int place, int clientId, CancellationToken token = default);
}