using System.Diagnostics;
using Bogus;
using Microsoft.AspNetCore.Mvc;

namespace OpenTelemetry.TemperatureApi.Web.Controllers;

[ApiController]
[Route("temperature")]
public class TemperatureController : ControllerBase
{
    private readonly ILogger<TemperatureController> _logger;
    private readonly Random _random;

    public TemperatureController(ILogger<TemperatureController> logger, Random random)
    {
        _logger = logger;
        _random = random;
    }

    [HttpGet("current")]
    public IActionResult Get()
    {
        _logger.LogInformation("Запрос на получение текущей температуры");
        var temp = _random.Next(-30, 30) + _random.NextDouble();
        _logger.LogInformation("Текущая температура: {Temp}", temp);
        return Ok(new
        {
            Temperature = temp
        });
    }
}