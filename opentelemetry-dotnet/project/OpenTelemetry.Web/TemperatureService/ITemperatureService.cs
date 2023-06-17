namespace OpenTelemetry.Web.TemperatureService;

public interface ITemperatureService
{
    public Task<double> GetTemperatureAsync(CancellationToken token);
}