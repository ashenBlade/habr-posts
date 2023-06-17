using Microsoft.EntityFrameworkCore;

namespace OpenTelemetry.RecordSaver.Worker.Database;

public class ApplicationDbContext: DbContext
{
    public DbSet<Record> Records => Set<Record>();

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options):base(options) { }
}