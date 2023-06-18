namespace OpenTelemetry.System.Web.TemperatureService;

public interface ITemperatureService
{
    public Task<double> GetTemperatureAsync(CancellationToken token);
}