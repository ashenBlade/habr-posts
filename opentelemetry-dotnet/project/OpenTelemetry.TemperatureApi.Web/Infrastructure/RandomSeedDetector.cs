using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.TemperatureApi.Web.Options;

namespace OpenTelemetry.TemperatureApi.Web.Infrastructure;

public class RandomSeedDetector: IResourceDetector
{
    private readonly IOptions<RandomOptions> _options;

    public RandomSeedDetector(IOptions<RandomOptions> options)
    {
        _options = options;
    }
    
    public Resource Detect()
    {
        return new Resource(new KeyValuePair<string, object>[]
        {
            new("random.seed", _options.Value.RandomSeed)
        });
    }
}