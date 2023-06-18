namespace OpenTelemetry.RecordSaver.Worker.Options;

public class TracingOptions
{
    [ConfigurationKeyName("TRACING_OLTP_ENDPOINT")]
    public Uri? OltpEndpoint { get; set; }

    [ConfigurationKeyName("TRACING_ZIPKIN_ENDPOINT")]
    public Uri? ZipkinEndpoint { get; set; }

    [ConfigurationKeyName("TRACING_JAEGER_AGENT_ENDPOINT")]
    public Uri? JaegerAgentEndpoint { get; set; }
}