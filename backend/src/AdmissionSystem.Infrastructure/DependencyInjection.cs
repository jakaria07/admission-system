using AdmissionSystem.Application.Classes;
using AdmissionSystem.Application.Common;
using AdmissionSystem.Application.Holds;
using AdmissionSystem.Infrastructure.BackgroundJobs;
using AdmissionSystem.Infrastructure.Persistence;
using AdmissionSystem.Infrastructure.Repositories;
using AdmissionSystem.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AdmissionSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = ResolveConnectionString(configuration);

        services.AddDbContext<AdmissionDbContext>(options =>
            options.UseNpgsql(connectionString));

        var holdDurationSeconds = ResolveHoldDurationSeconds(configuration);
        services.AddSingleton(new HoldOptions
        {
            HoldDurationSeconds = holdDurationSeconds
        });

        services.AddScoped<IClock, DbClock>();
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IHoldRepository, HoldRepository>();
        services.AddHostedService<HoldExpiryWorker>();

        return services;
    }

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var connectionString =
            configuration.GetConnectionString("Default") ??
            configuration["DATABASE_CONNECTION_STRING"] ??
            configuration["DB_CONNECTION_STRING"] ??
            configuration["DATABASE_URL"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Host=localhost;Port=5432;Database=admission_db;Username=postgres;Password=postgres";
        }

        return connectionString;
    }

    private static int ResolveHoldDurationSeconds(IConfiguration configuration)
    {
        var raw = configuration["HOLD_DURATION_SECONDS"] ??
                  configuration["HoldSettings:HoldDurationSeconds"];

        if (int.TryParse(raw, out var seconds) && seconds > 0)
        {
            return seconds;
        }

        return 600;
    }
}

