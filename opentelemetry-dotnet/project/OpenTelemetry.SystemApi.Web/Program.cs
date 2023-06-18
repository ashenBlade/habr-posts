using Confluent.Kafka;
using Microsoft.Extensions.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.System.Web.Infrastructure;
using OpenTelemetry.System.Web.Options;
using OpenTelemetry.System.Web.TemperatureService;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IProducer<Null, string>>(sp =>
{
    var applicationOptions = sp.GetRequiredService<IOptions<ApplicationOptions>>().Value;
    var producer = new ProducerBuilder<Null, string>(new ProducerConfig()
        {
            BootstrapServers = applicationOptions.BootstrapServers,
        })
       .Build();
    producer = new TracingProducerDecorator<Null, string>( producer, applicationOptions.SendRandomBaggage,
        sp.GetRequiredService<ILogger<TracingProducerDecorator<Null, string>>>());
    return producer;
});

builder.Services
       .AddOpenTelemetry()
       .WithTracing(tracing =>
        {
            var options = builder.Configuration.Get<TracingOptions>();
            if (options.OltpEndpoint is {} oltpEndpoint)
            {
                tracing.AddOtlpExporter(oltp =>
                {
                    oltp.Endpoint = oltpEndpoint;
                });
            }

            if (options.ZipkinEndpoint is {} zipkinEndpoint)
            {
                tracing.AddZipkinExporter(zipkin =>
                {
                    zipkin.Endpoint = zipkinEndpoint;
                });
            }

            if (options.JaegerEndpoint is {} jaegerEndpoint)
            {
                tracing.AddJaegerExporter(jaeger =>
                {
                    jaeger.Endpoint = jaegerEndpoint;
                });
            }
            
            tracing.AddAspNetCoreInstrumentation()
                   .AddHttpClientInstrumentation()

                   .ConfigureResource(r => r.AddService("OpenTelemetry.SystemApi.Web"))
                   .AddSource(Tracing.WebActivitySource.Name);
            
        });

builder.Services
       .AddOptions<ApplicationOptions>()
       .Bind(builder.Configuration);

const string temperatureHttpClientName = "TemperatureHttpClient";

builder.Services.AddHttpClient(temperatureHttpClientName, client =>
{
    client.BaseAddress = new Uri("http://temperature.api:5000");
});

builder.Services.AddScoped<ITemperatureService>(sp =>
{
    var client = sp.GetRequiredService<IHttpClientFactory>()
                   .CreateClient(temperatureHttpClientName);
    var temperatureService = new HttpClientTemperatureService(client, sp.GetRequiredService<ILogger<HttpClientTemperatureService>>());
    return new JsonExceptionEventRecorderServiceDecorator( temperatureService );
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();