namespace OpenTelemetry.RecordSaver.Worker;

public class ApplicationOptions
{
    [ConfigurationKeyName("TRACING_USE_LINK")]
    public bool UseLink { get; set; } = false;
    
    [ConfigurationKeyName("TRACING_LOG_BAGGAGE")]
    public bool LogBaggageOnContextExtraction { get; set; } = false;
}