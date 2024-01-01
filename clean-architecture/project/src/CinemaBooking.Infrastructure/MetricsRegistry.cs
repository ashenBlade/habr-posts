using System.Diagnostics.Metrics;

namespace CinemaBooking.Infrastructure;

public static class MetricsRegistry
{
    public static readonly Meter AppMeter = new Meter("CinemaBooking", "1.0.0");
    public static readonly Counter<long> BoughtSeatsCount = AppMeter.CreateCounter<long>(
        name: "seats-bought-count",
        unit: null,
        description: "Количество купленных мест");

    public static readonly Counter<long> BookedSeatsCount = AppMeter.CreateCounter<long>(
        name: "booked-seats-count",
        unit: null,
        description: "Количество забронированных мест"); 
}