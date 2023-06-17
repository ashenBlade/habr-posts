using System.Diagnostics;
using System.Globalization;
using System.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Web.TemperatureService;

namespace OpenTelemetry.Web.Decorators;

public class SomeTemperatureServiceDecorator: ITemperatureService
{
    private readonly ITemperatureService _service;

    public SomeTemperatureServiceDecorator(ITemperatureService service)
    {
        _service = service;
    }

    public async Task<double> GetTemperatureAsync(CancellationToken token)
    {
        var result = await _service.GetTemperatureAsync(token);
        
        if (Activity.Current is {} activity)
        {
            activity.SetTag("recorded.temperature", new[] {1, 2, 3, 4, 5});
        }
        
        return result;
    }
}