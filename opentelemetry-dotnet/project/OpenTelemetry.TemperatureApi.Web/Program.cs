using System.Diagnostics;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.TemperatureApi.Web.Infrastructure;
using OpenTelemetry.TemperatureApi.Web.Options;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
       .AddOpenTelemetry()
       .WithTracing(tracing =>
        {
            tracing.AddAspNetCoreInstrumentation()
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
                    //  })
                   
                   .ConfigureResource(rb =>
                    {
                        rb.AddService(
                            serviceName: "TemperatureApi",
                            serviceVersion: "1.0.1",
                            autoGenerateServiceInstanceId: true);
                        rb.AddEnvironmentVariableDetector();
                        rb.AddDetector(sp =>
                            new RandomSeedDetector(sp.GetRequiredService<IOptions<RandomOptions>>()));
                    })
                   .AddHttpClientInstrumentation();
        });

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();