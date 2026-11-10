using AdmissionSystem.Domain.Classes;
using AdmissionSystem.Domain.Holds;
using Microsoft.EntityFrameworkCore;

namespace AdmissionSystem.Infrastructure.Persistence;

public sealed class AdmissionDbContext : DbContext
{
    public AdmissionDbContext(DbContextOptions<AdmissionDbContext> options) : base(options)
    {
    }

    public DbSet<AdmissionClass> AdmissionClasses => Set<AdmissionClass>();
    public DbSet<AdmissionHold> AdmissionHolds => Set<AdmissionHold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AdmissionDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}

