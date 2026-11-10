using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AdmissionSystem.Infrastructure.Persistence;

public sealed class AdmissionDbContextFactory : IDesignTimeDbContextFactory<AdmissionDbContext>
{
    public AdmissionDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AdmissionDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=admission_db;Username=postgres;Password=postgres");
        return new AdmissionDbContext(optionsBuilder.Options);
    }
}

