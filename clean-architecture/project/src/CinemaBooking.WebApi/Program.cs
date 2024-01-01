using System.Text.Json;
using System.Text.Json.Serialization;
using CinemaBooking.Domain;
using CinemaBooking.Services.SessionRepository;
using Microsoft.EntityFrameworkCore;

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
       .AddScoped<IBookingService>(sp => new BookingService(sp.GetRequiredService<ISessionRepository>()));

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();

app.Run();