using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Bogus;
using Confluent.Kafka;
using Grpc.Core;
using Microsoft.AspNetCore.Mvc;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Trace;
using OpenTelemetry.Web.Infrastructure;
using OpenTelemetry.Web.Models;
using OpenTelemetry.Web.TemperatureService;
using Status = OpenTelemetry.Trace.Status;

namespace OpenTelemetry.Web.Controllers;

[ApiController]
[Route("[controller]")]
public class SystemController : ControllerBase
{
    private readonly IProducer<Null, string> _producer;
    private readonly ITemperatureService _temperatureService;
    public static readonly Faker Faker = new Faker("ru");

    public SystemController(IProducer<Null, string> producer, ITemperatureService temperatureService)
    {
        _producer = producer;
        _temperatureService = temperatureService;
    }

    [HttpGet("state")]
    public async Task<IActionResult> ProduceData(CancellationToken token)
    {
        // ReSharper disable once ExplicitCallerInfoArgument
        using var activity = Tracing.WebActivitySource.StartActivity(Tracing.StateRequest, ActivityKind.Server);
        var temp = await _temperatureService.GetTemperatureAsync(token);
        var measurement = new WeatherForecast()
        {
            Id = Guid.NewGuid(),
            Date = DateTime.Now,
            Summary = Faker.Random.Words(5),
            TemperatureC = temp
        };
        
        var serialized = JsonSerializer.Serialize(measurement);
        
        await _producer.ProduceAsync("weather", new Message<Null, string>()
        {
            Value = serialized
        }, token);
        
        return Ok(new
        {
            IsHealthy = measurement.TemperatureC <= 20,
            Description = measurement.Summary
        });
    }
}