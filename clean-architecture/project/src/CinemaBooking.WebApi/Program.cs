using System.Text.Json.Serialization;
using CinemaBooking.Domain;
using CinemaBooking.Grpc;
using CinemaBooking.Infrastructure;
using CinemaBooking.Services.SessionRepository;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using SeatService = CinemaBooking.Domain.SeatService;

var builder = WebApplication.CreateBuilder(args);

builder.Services
       .AddControllers()
       .AddJsonOptions(json =>
        {
            json.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services
       .AddDbContext<SessionDbContext>((sp, options) =>
        {
            options.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("PostgresSessionsDb"));
        });

builder.Services
       .AddScoped<ISessionRepository, PostgresSessionRepository>();

builder.Services
       .AddScoped<ISeatService>(sp =>
        {
            ISeatService seatService = new SeatService(sp.GetRequiredService<ISessionRepository>());
            seatService = new MetricScrapperSeatService(seatService);
            return seatService;
        });

builder.Services
       .AddOpenTelemetry()
       .WithMetrics(metrics => metrics.AddPrometheusExporter()
                                      .AddMeter(MetricsRegistry.AppMeter.Name))
       .ConfigureResource(rb => rb.AddService("CinemaBooking", serviceVersion: "1.0.0"));

builder.Services.AddGrpc();

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

app.MapControllers();
app.MapGrpcService<GrpcSeatService>();

app.MapPrometheusScrapingEndpoint("/metrics");

app.Run();