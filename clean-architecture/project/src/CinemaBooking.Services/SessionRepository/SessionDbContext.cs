using Microsoft.EntityFrameworkCore;

namespace CinemaBooking.Services.SessionRepository;

public class SessionDbContext: DbContext
{
    public DbSet<DatabaseSession> Sessions => Set<DatabaseSession>();
    public DbSet<DatabaseSeat> Seats => Set<DatabaseSeat>();

    public SessionDbContext(DbContextOptions<SessionDbContext> options): base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DatabaseSeat>()
                    .HasKey(seat => new {seat.SessionId, seat.Number});
    }
}