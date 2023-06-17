using Microsoft.EntityFrameworkCore;
using OpenTelemetry.RecordSaver.Worker;
using OpenTelemetry.RecordSaver.Worker.Database;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var host = Host.CreateDefaultBuilder(args)
               .ConfigureServices(services =>
                {
                    services.AddHostedService<KafkaConsumer>();
                    services
                       .AddOpenTelemetry()
                       .WithTracing(tracing =>
                        {
                            tracing.AddAspNetCoreInstrumentation()
                                   .AddEntityFrameworkCoreInstrumentation()
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
                                     
                                   .ConfigureResource(r => r.AddService(Tracing.ConsumerActivitySource.Name))
                                   .AddSource(Tracing.ConsumerActivitySource.Name);
                        });
                    services.AddSingleton(TracerProvider.Default.GetTracer(Tracing.ConsumerActivitySource.Name));
                    services.AddDbContextFactory<ApplicationDbContext>(db =>
                    {
                        db.UseNpgsql("Host=postgres;Database=postgres;User Id=postgres;Password=postgres");
                    });
                })
               .Build();

await using (var x = host.Services.CreateAsyncScope())
{
    await x.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
}
await host.RunAsync();