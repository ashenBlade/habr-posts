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
                   .ConfigureResource(rb =>
                    {
                        var name = typeof(TemperatureController).Assembly.GetName();
                        rb.AddService(
                            serviceName: name.Name!,
                            serviceVersion: name.Version!.ToString(),
                            autoGenerateServiceInstanceId: true);
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
       .Bind(builder.Configuration)
       .ValidateDataAnnotations();

builder.Services.AddOptions<TracingOptions>()
       .Bind(builder.Configuration);

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();

app.Run();