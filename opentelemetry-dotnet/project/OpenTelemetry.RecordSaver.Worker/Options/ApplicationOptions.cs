using System.ComponentModel.DataAnnotations;

namespace OpenTelemetry.RecordSaver.Worker.Options;

public class ApplicationOptions
{
    [ConfigurationKeyName("TRACING_USE_LINK")]
    public bool UseLink { get; set; } = false;
    
    [ConfigurationKeyName("TRACING_LOG_BAGGAGE")]
    public bool LogBaggageOnContextExtraction { get; set; } = false;

    [ConfigurationKeyName("KAFKA_BOOTSTRAP_SERVERS")]
    [Required]
    public string BootstrapServers { get; set; } = null!;
    
    [ConfigurationKeyName("KAFKA_QUEUE")]
    [Required]
    public string KafkaQueue { get; set; } = null!;

}