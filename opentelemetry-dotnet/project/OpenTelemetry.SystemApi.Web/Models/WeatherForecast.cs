namespace OpenTelemetry.System.Web.Models;

public class WeatherForecast
{
    public Guid Id { get; set; }
    public DateTime Date { get; set; }

    public double TemperatureC { get; set; }

    public int TemperatureF => 32 + ( int ) ( TemperatureC / 0.5556 );

    public string? Summary { get; set; }
}