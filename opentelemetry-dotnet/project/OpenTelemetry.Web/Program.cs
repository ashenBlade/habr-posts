using System.Diagnostics;
using Confluent.Kafka;
using Microsoft.Extensions.Options;
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

builder.Services.AddSingleton<IProducer<Null, string>>(sp =>
{
    var producer = new ProducerBuilder<Null, string>(new ProducerConfig()
        {
            BootstrapServers = "kafka:9092",
        })
       .Build();
    producer = new TracingProducerDecorator<Null, string>( producer, 
        sp.GetRequiredService<IOptions<ApplicationOptions>>(),
        sp.GetRequiredService<ILogger<TracingProducerDecorator<Null, string>>>());
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