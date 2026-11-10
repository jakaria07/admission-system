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
using Npgsql;

namespace AdmissionSystem.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AdmissionDbContext>((sp, options) =>
        {
            var effectiveConfig = sp.GetRequiredService<IConfiguration>();
            options.UseNpgsql(ResolveConnectionString(effectiveConfig));
        });

        services.AddScoped<HoldOptions>(sp =>
        {
            var effectiveConfig = sp.GetRequiredService<IConfiguration>();
            return new HoldOptions
            {
                HoldDurationSeconds = ResolveHoldDurationSeconds(effectiveConfig)
            };
        });

        services.AddScoped<IClock, DbClock>();
        services.AddScoped<IClassRepository, ClassRepository>();
        services.AddScoped<IHoldRepository, HoldRepository>();
        services.AddHostedService<HoldExpiryWorker>();

        return services;
    }

    public static string ResolveConnectionString(IConfiguration configuration)
    {
        var raw =
            configuration.GetConnectionString("Default") ??
            configuration["DATABASE_CONNECTION_STRING"] ??
            configuration["DB_CONNECTION_STRING"] ??
            configuration["DATABASE_URL"];

        if (string.IsNullOrWhiteSpace(raw))
        {
            return "Host=localhost;Port=5432;Database=admission_db;Username=postgres;Password=postgres";
        }

        return NormalizeConnectionString(raw.Trim());
    }

    public static string NormalizeConnectionString(string raw)
    {
        NpgsqlConnectionStringBuilder builder;

        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw);
            builder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = uri.AbsolutePath.Trim('/')
            };

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var userInfoParts = uri.UserInfo.Split(':', 2);
                builder.Username = Uri.UnescapeDataString(userInfoParts[0]);
                if (userInfoParts.Length > 1)
                {
                    builder.Password = Uri.UnescapeDataString(userInfoParts[1]);
                }
            }

            if (!string.IsNullOrWhiteSpace(uri.Query))
            {
                var queryParams = uri.Query.TrimStart('?')
                    .Split('&', StringSplitOptions.RemoveEmptyEntries);

                foreach (var param in queryParams)
                {
                    var kv = param.Split('=', 2);
                    if (kv.Length == 2)
                    {
                        var key = Uri.UnescapeDataString(kv[0]).Replace("_", " ");
                        var val = Uri.UnescapeDataString(kv[1]);
                        builder[key] = val;
                    }
                }
            }
        }
        else
        {
            builder = new NpgsqlConnectionStringBuilder(raw);
        }

        // PostgreSQL default max_connections is 100. When running multiple backend processes
        // sharing a single PostgreSQL server under heavy concurrency, cap each process's pool
        // at 40 so two processes + test runner never exceed PostgreSQL's 100-connection ceiling.
        if (!raw.Contains("Maximum Pool Size", StringComparison.OrdinalIgnoreCase) &&
            !raw.Contains("MaxPoolSize", StringComparison.OrdinalIgnoreCase))
        {
            builder.MaxPoolSize = 40;
        }

        return builder.ConnectionString;
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

