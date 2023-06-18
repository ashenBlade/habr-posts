using System.Diagnostics;
using Bogus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OpenTelemetry.TemperatureApi.Web.Options;

namespace OpenTelemetry.TemperatureApi.Web.Controllers;

[ApiController]
[Route("temperature")]
public class TemperatureController : ControllerBase
{
    private readonly ILogger<TemperatureController> _logger;
    private readonly IOptions<ApplicationOptions> _options;
    private readonly Random _random;

    public TemperatureController(ILogger<TemperatureController> logger, IOptions<ApplicationOptions> options, Random random)
    {
        _logger = logger;
        _options = options;
        _random = random;
    }

    [HttpGet("current")]
    public IActionResult Get()
    {
        if (_options.Value.ThrowException)
        {
            var exception = new ApplicationException("Надо было выкинуть исключение. Извини((");
            if (Activity.Current is {} activity)
            {
                var tags = new ActivityTagsCollection(new KeyValuePair<string, object?>[]
                {
                    new("exception.type", exception.GetType().Name), 
                    new("exception.message", exception.Message),
                    new("exception.stacktrace", exception.StackTrace)
                });
                activity.AddEvent(new ActivityEvent("exception", tags: tags));
                activity.SetStatus(ActivityStatusCode.Error);
            }

            throw exception;
        }
        _logger.LogInformation("Запрос на получение текущей температуры");
        var temp = _random.Next(-30, 30) + _random.NextDouble();
        _logger.LogInformation("Текущая температура: {Temp}", temp);
        return Ok(new
        {
            Temperature = temp
        });
    }
}