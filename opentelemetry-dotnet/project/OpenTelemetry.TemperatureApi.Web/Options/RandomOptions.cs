namespace OpenTelemetry.TemperatureApi.Web.Options;

public class RandomOptions
{
    [ConfigurationKeyName("RANDOM_SEED")]
    public int RandomSeed { get; set; } = 42;
}