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
        OperationResultCode code;
        try
        {
            await _service.BookSeatAsync(request.SessionId, request.SeatNumber, request.UserId,
                context.CancellationToken);
            code = OperationResultCode.Ok;
        }
        catch (SessionNotFoundException)
        {
            code = OperationResultCode.SessionNotFound;
        }
        catch (SeatNotFoundException)
        {
            code = OperationResultCode.SeatNotFound;
        }
        catch (SeatBoughtException)
        {
            code = OperationResultCode.SeatBought;
        }
        catch (SeatBookedException)
        {
            code = OperationResultCode.SeatBought;
        }

        return new BookResponse() {ResultCode = code};
    }

    public override async Task<BuyResponse> BuySeat(BuyRequest request, ServerCallContext context)
    {
        OperationResultCode code;
        try
        {
            await _service.BuySeatAsync(request.SessionId, request.SeatNumber, request.UserId, context.CancellationToken);
            code = OperationResultCode.Ok;
        }
        catch (SessionNotFoundException)
        {
            code = OperationResultCode.SessionNotFound;
        }
        catch (SeatNotFoundException)
        {
            code = OperationResultCode.SeatNotFound;
        }
        catch (SeatBoughtException)
        {
            code = OperationResultCode.SeatBought;
        }
        catch (SeatBookedException)
        {
            code = OperationResultCode.SeatBought;
        }

        return new BuyResponse() {ResultCode = code};
    }
}