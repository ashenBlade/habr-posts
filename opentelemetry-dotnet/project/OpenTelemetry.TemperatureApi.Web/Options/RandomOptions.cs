namespace OpenTelemetry.TemperatureApi.Web.Options;

public class RandomOptions
{
    [ConfigurationKeyName("SEED")]
    public int RandomSeed { get; set; } = 42;
}