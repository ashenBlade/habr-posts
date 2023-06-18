using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.TemperatureApi.Web.Controllers;
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
                        var name = typeof(TemperatureController).Assembly.GetName();
                        rb.AddService(
                            serviceName: name.Name!,
                            serviceVersion: name.Version!.ToString(),
                            autoGenerateServiceInstanceId: true);
                        rb.AddEnvironmentVariableDetector();
                        rb.AddDetector(sp =>
                            new RandomSeedDetector(sp.GetRequiredService<IOptions<RandomOptions>>()));
                    });
        });

builder.Services
       .AddOptions<RandomOptions>()
       .Bind(builder.Configuration);

builder.Services
       .AddSingleton(sp => new Random(sp.GetRequiredService<IOptions<RandomOptions>>().Value.RandomSeed));

builder.Services
       .AddOptions<ApplicationOptions>()
       .Bind(builder.Configuration);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();