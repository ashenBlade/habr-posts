using System.ComponentModel.DataAnnotations;

namespace OpenTelemetry.TemperatureApi.Web.Options;

public class ApplicationOptions
{
    [ConfigurationKeyName("THROW_EXCEPTION")]
    public bool ThrowException { get; set; } = false;
}