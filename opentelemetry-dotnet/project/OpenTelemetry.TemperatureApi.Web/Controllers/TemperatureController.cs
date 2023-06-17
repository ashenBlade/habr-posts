using System.Diagnostics;
using Bogus;
using Microsoft.AspNetCore.Mvc;

namespace OpenTelemetry.TemperatureApi.Web.Controllers;

[ApiController]
[Route("temperature")]
public class TemperatureController : ControllerBase
{
    private static readonly Faker Faker = new Faker("ru");
    private readonly ILogger<TemperatureController> _logger;

    public TemperatureController(ILogger<TemperatureController> logger)
    {
        _logger = logger;
    }

    [HttpGet("current")]
    public IActionResult Get()
    {
        Activity.Current?.SetStatus(ActivityStatusCode.Error);
        _logger.LogInformation("Запрос на получение текущей температуры");
        var temp = Faker.Random.Int(-30, 30) + Faker.Random.Double();
        _logger.LogInformation("Текущая температура: {Temp}", temp);
        return Ok(new {Temperature = temp});
    }
}