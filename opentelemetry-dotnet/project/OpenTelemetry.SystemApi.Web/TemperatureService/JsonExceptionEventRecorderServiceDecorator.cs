using System.Diagnostics;
using System.Text.Json;
using OpenTelemetry.Trace;

namespace OpenTelemetry.System.Web.TemperatureService;

public class JsonExceptionEventRecorderServiceDecorator: ITemperatureService
{
    private readonly ITemperatureService _service;

    public JsonExceptionEventRecorderServiceDecorator(ITemperatureService service)
    {
        _service = service;
    }

    public async Task<double> GetTemperatureAsync(CancellationToken token)
    {
        try
        {
            return await _service.GetTemperatureAsync(token);
        }
        catch (JsonException e) when (Activity.Current is { } activity)
        {
            var @event = new ActivityEvent("Ошибка парсинга JSON",
                tags: new ActivityTagsCollection(new KeyValuePair<string, object?>[] {new("json.error.path", e.Path)}));
            activity.AddEvent(@event);
            throw;
        }
        catch (Exception e) when (Activity.Current is { } activity)
        {
            activity.RecordException(e);
            activity.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }
}