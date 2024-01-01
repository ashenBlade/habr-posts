using CinemaBooking.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CinemaBooking.WebApi.Filters;

public class DomainExceptionFilterAttribute: ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not DomainException domainException)
        {
            return;
        }
    
        var responseObject = new {message = domainException.Message};
        context.Result = domainException switch
                         {
                             SeatNotFoundException    => new NotFoundObjectResult(responseObject),
                             SessionNotFoundException => new NotFoundObjectResult(responseObject),
                             _                        => new BadRequestObjectResult(responseObject)
                         };
    
        context.ExceptionHandled = true;
    }
}