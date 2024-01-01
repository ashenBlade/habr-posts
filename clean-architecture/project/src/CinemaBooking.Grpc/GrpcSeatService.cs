using CinemaBooking.Domain;
using CinemaBooking.Domain.Exceptions;
using Grpc.Core;

namespace CinemaBooking.Grpc;

public class GrpcSeatService: SeatService.SeatServiceBase
{
    private readonly ISeatService _service;

    public GrpcSeatService(ISeatService service)
    {
        _service = service;
    }
    
    public override async Task<BookResponse> BookSeat(BookRequest request, ServerCallContext context)
    {
        var code = await ExecuteGetResultCodeAsync(t => _service.BookSeatAsync(request.SessionId, request.SeatNumber, request.UserId, t), context.CancellationToken);
        return new BookResponse() {ResultCode = code};
    }

    public override async Task<BuyResponse> BuySeat(BuyRequest request, ServerCallContext context)
    {
        var code = await ExecuteGetResultCodeAsync(t => _service.BuySeatAsync(request.SessionId, request.SeatNumber, request.UserId, t), context.CancellationToken);
        return new BuyResponse() {ResultCode = code};
    }

    private static async Task<OperationResultCode> ExecuteGetResultCodeAsync(Func<CancellationToken, Task> code, CancellationToken token)
    {
        try
        {
            await code(token);
            return OperationResultCode.Ok;
        }
        catch (SessionNotFoundException)
        {
            return OperationResultCode.SessionNotFound;
        }
        catch (SeatNotFoundException)
        {
            return OperationResultCode.SeatNotFound;
        }
        catch (SeatBoughtException)
        {
            return OperationResultCode.SeatBought;
        }
        catch (SeatBookedException)
        {
            return OperationResultCode.SeatBought;
        }

    }
}