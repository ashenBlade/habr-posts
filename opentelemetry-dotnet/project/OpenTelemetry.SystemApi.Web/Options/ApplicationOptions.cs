using System.ComponentModel.DataAnnotations;

namespace OpenTelemetry.System.Web.Options;

public class ApplicationOptions
{
    [ConfigurationKeyName("TRACING_SEND_RANDOM_BAGGAGE")]
    public bool SendRandomBaggage { get; set; } = false;

    [ConfigurationKeyName("KAFKA_BOOTSTRAP_SERVERS")]
    [Required]
    public string BootstrapServers { get; set; } = null!;

    [ConfigurationKeyName("KAFKA_QUEUE")]
    [Required]
    public string KafkaQueue { get; set; } = null!;
}