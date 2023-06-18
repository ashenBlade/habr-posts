namespace OpenTelemetry.System.Web.TemperatureService;

public class HttpClientTemperatureService: ITemperatureService
{
    private readonly HttpClient _client;
    private readonly ILogger<HttpClientTemperatureService> _logger;

    public HttpClientTemperatureService(HttpClient client, ILogger<HttpClientTemperatureService> logger)
    {
        _client = client;
        _logger = logger;
    }
    
    public async Task<double> GetTemperatureAsync(CancellationToken token)
    {
        _logger.LogInformation("Делаю запрос для получения температуры");
        var response = await _client.GetFromJsonAsync<TemperatureRecord>("/temperature/current", token);
        return response?.Temperature ?? throw new ArgumentNullException(nameof(TemperatureRecord.Temperature));
    }

    public class TemperatureRecord
    {
        public double Temperature { get; set; }
    }
}