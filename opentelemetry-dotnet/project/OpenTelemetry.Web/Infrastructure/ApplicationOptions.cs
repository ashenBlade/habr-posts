namespace OpenTelemetry.Web.Infrastructure;

public class ApplicationOptions
{
    [ConfigurationKeyName("TRACING_SEND_RANDOM_BAGGAGE")]
    public bool SendRandomBaggage { get; set; } = false;
}