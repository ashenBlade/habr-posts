using CinemaBooking.Console;
using CinemaBooking.Domain;
using CinemaBooking.Domain.Exceptions;
using CinemaBooking.Services.SessionRepository;
using Microsoft.EntityFrameworkCore;

CommandLineArguments arguments;
try
{
    arguments = CommandLineArguments.FromCommandLineArguments(args);
}
catch (InvalidOperationException)
{
    PrintUsage();
    return 1;
}

await using var database = GetDatabaseConnection();
var repo = new PostgresSessionRepository(database);
var seatService = new SeatService(repo);
var (command, sessionId, seat, clientId) = arguments;

var responseCode = 0;
try
{
    switch (command)
    {
        case OperationType.Book:
            try
            {
                await seatService.BookSeatAsync(sessionId, seat, clientId);
                Console.WriteLine($"Место забронировано");
            }
            catch (SeatBookedException e) when (e.ClientId == clientId)
            {
                Console.WriteLine($"Вы уже забронировали это место");
            }

            break;
        case OperationType.Buy:
            try
            {
                await seatService.BuySeatAsync(sessionId, seat, clientId);
                Console.WriteLine($"Место куплено");
            }
            catch (SeatBoughtException e) when (e.ClientId == clientId)
            {
                Console.WriteLine($"Вы уже купили это место");
            }

            break;
        default:
            throw new ArgumentOutOfRangeException(nameof(command), command, "Неизвестная команда");
    }
}
catch (SeatNotFoundException snf)
{
    Console.WriteLine($"Место {snf.Seat} не найдено");
    responseCode = 2;
}
catch (SessionNotFoundException snf)
{
    Console.WriteLine($"Сеанс {snf.SessionId} не найден");
    responseCode = 3;
}
catch (SeatBookedException)
{
    Console.WriteLine($"Указанное место забронировано за другим посетителем");
    responseCode = 4;
}
catch (SeatBoughtException)
{
    Console.WriteLine($"Указанное место куплено другим посетителем");
    responseCode = 5;
}

return responseCode;

void PrintUsage()
{
    var usage = """
                Использование: *prog_name* OPERATION SESSION_ID SEAT_NUMBER CLIENT_ID

                OPERATION - операция, которую нужно выполнить:
                    1. book - забронировать место
                    2. buy - купить место
                SESSION_ID - ID сеанса
                SEAT_NUMBER - номер места
                CLIENT_ID - ID клиента

                Примеры:
                - book 12 34 23 - забронировать 34 место для сеанса 12 на клиента 23
                - buy 542 24 77 - купить 24 место для сеанса 542 клиенту 77
                """;
    Console.WriteLine(usage);
}

SessionDbContext GetDatabaseConnection()
{
    var builder = new DbContextOptionsBuilder<SessionDbContext>();
    builder.UseNpgsql("Host=localhost;Port=5432;Database=postgres;User Id=postgres;Password=postgres");
    return new SessionDbContext(builder.Options);
}