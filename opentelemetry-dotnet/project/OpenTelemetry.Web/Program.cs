using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Web.Decorators;
using OpenTelemetry.Web.Infrastructure;
using OpenTelemetry.Web.TemperatureService;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IProducer<Null, string>>(_ =>
{
    var producer = new ProducerBuilder<Null, string>(new ProducerConfig()
        {
            BootstrapServers = "kafka:9092",
        })
       .Build();
    producer = new TracingProducerDecorator<Null, string>( producer );
    return producer;
});

builder.Services
       .AddOpenTelemetry()
       .WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation()
                   .AddHttpClientInstrumentation()
                   .AddOtlpExporter(oltp =>
                    {
                        oltp.Endpoint = new Uri("http://jaeger:4317");
                        oltp.Protocol = OtlpExportProtocol.Grpc;
                        oltp.ExportProcessorType = ExportProcessorType.Batch;
                    })

                    // .AddZipkinExporter(zipkin =>
                    //  {
                    //      zipkin.Endpoint = new Uri("http://zipkin:9411/api/v2/spans");
                    //      zipkin.ExportProcessorType = ExportProcessorType.Batch;
                    //  })

                    // .AddJaegerExporter(jaeger =>
                    //  {
                    //      jaeger.AgentHost = "jaeger";
                    //      jaeger.AgentPort = 6831;
                    //      jaeger.Protocol = JaegerExportProtocol.UdpCompactThrift;
                    //      jaeger.ExportProcessorType = ExportProcessorType.Batch;
                    //  })

                   .ConfigureResource(r => r.AddService("OpenTelemetry.Web"))
                   .AddSource(Tracing.WebActivitySource.Name);
        });

builder.Services.AddSingleton(TracerProvider.Default.GetTracer(Tracing.WebActivitySource.Name));

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
    return new SomeTemperatureServiceDecorator( temperatureService );
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();