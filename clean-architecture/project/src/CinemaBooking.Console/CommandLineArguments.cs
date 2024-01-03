namespace CinemaBooking.Console;

public record CommandLineArguments(OperationType Command, int SessionId, int SeatNumber, int ClientId)
{
    public static CommandLineArguments FromCommandLineArguments(string[] arguments)
    {
        if (arguments.Length != 4)
        {
            throw new InvalidOperationException();
        }

        var (operation, session, seat, client) = ( arguments[0], arguments[1], arguments[2], arguments[3] );
        OperationType command;
        if (operation.Equals("buy", StringComparison.InvariantCultureIgnoreCase))
        {
            command = OperationType.Buy;
        }
        else if (operation.Equals("book", StringComparison.InvariantCultureIgnoreCase))
        {
            command = OperationType.Book;
        }
        else
        {
            throw new InvalidOperationException();
        }

        if (!int.TryParse(session, out var sessionId))
        {
            throw new InvalidOperationException();
        }
        
        if (!int.TryParse(seat, out var seatNumber))
        {
            throw new InvalidOperationException();
        }
        
        if (!int.TryParse(client, out var clientId))
        {
            throw new InvalidOperationException();
        }

        return new CommandLineArguments(command, sessionId, seatNumber, clientId);
    }
}