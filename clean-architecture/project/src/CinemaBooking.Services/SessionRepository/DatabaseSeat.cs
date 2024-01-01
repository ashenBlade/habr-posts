using CinemaBooking.Domain;

namespace CinemaBooking.Services.SessionRepository;

public class DatabaseSeat
{
    public int SessionId { get; set; }
    public DatabaseSession Session { get; set; }
    public int Number { get; set; }
    public SeatType Type { get; set; }
    public int? ClientId { get; set; }

    public Seat ToDomainSeat()
    {
        switch (Type)
        {
            case SeatType.Free:
                return new FreeSeat(Number);
            case SeatType.Booked:
                if (ClientId is null)
                {
                    throw new InvalidOperationException(
                        $"Место для сеанса {SessionId} под номером {Number} помечено забронированным, но Id клиента отсутствует");
                }

                return new BookedSeat(Number, ClientId.Value);
            case SeatType.Bought:
                if (ClientId is null)
                {
                    throw new InvalidOperationException(
                        $"Место для сеанса {SessionId} под номером {Number} помечено купленным, но Id клиента отсутствует");
                }

                return new BoughtSeat(Number, ClientId.Value);
        }

        throw new ArgumentOutOfRangeException(nameof(Type), Type, "Неизвестный тип места");
    }
}