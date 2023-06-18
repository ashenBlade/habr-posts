using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.RecordSaver.Worker;
using OpenTelemetry.RecordSaver.Worker.Database;
using OpenTelemetry.RecordSaver.Worker.HostedServices;
using OpenTelemetry.RecordSaver.Worker.Infrastructure;
using OpenTelemetry.RecordSaver.Worker.Options;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var host = Host.CreateDefaultBuilder(args)
               .ConfigureServices((context, services) =>
                {
                    services.AddHostedService<KafkaConsumerBackgroundService>();
                    services
                       .AddOpenTelemetry()
                       .WithTracing(tracing =>
                        {
                            var options = context.Configuration.Get<TracingOptions>();
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

                            if (options.JaegerAgentEndpoint is {} jaegerEndpoint)
                            {
                                tracing.AddJaegerExporter(jaeger =>
                                {
                                    jaeger.AgentPort = jaegerEndpoint.Port;
                                    jaeger.AgentHost = jaegerEndpoint.Host;
                                });
                            }
                            tracing.AddAspNetCoreInstrumentation()
                                   .AddEntityFrameworkCoreInstrumentation()
                                   .ConfigureResource(r =>
                                    {
                                        var assemblyName = typeof(KafkaConsumerBackgroundService).Assembly.GetName();
                                        var name = assemblyName.Name!;
                                        var version = assemblyName.Version?.ToString()!;
                                        r.AddService(serviceName: name, serviceVersion: version);
                                    })
                                   .AddSource(Tracing.ConsumerActivitySource.Name);
                        });
                    services.AddDbContextFactory<ApplicationDbContext>((sp, db) =>
                    {
                        var options = sp.GetRequiredService<IOptions<ApplicationOptions>>().Value;
                        db.UseNpgsql(options.ConnectionString);
                    });
                    services.AddOptions<ApplicationOptions>()
                            .Bind(context.Configuration)
                            .ValidateDataAnnotations();
                    
                    services.AddOptions<TracingOptions>()
                            .Bind(context.Configuration)
                            .ValidateDataAnnotations();
                })
               .Build();

await using (var x = host.Services.CreateAsyncScope())
{
    await x.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
}
await host.RunAsync();