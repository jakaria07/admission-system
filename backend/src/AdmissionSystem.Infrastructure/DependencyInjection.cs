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
        // Primary documented configuration is ConnectionStrings__Default (or ConnectionStrings:Default).
        // Fallbacks for common container/cloud environment variables are checked subsequently.
        var raw =
            configuration.GetConnectionString("Default") ??
            configuration["DATABASE_CONNECTION_STRING"] ??
            configuration["DB_CONNECTION_STRING"] ??
            configuration["DATABASE_URL"];

        if (string.IsNullOrWhiteSpace(raw))
        {
            raw = "Host=localhost;Port=5432;Database=admission_db;Username=postgres;Password=postgres";
        }

        int? configuredMaxPoolSize = null;
        if (int.TryParse(configuration["MAX_POOL_SIZE"], out var parsedPool) && parsedPool > 0)
        {
            configuredMaxPoolSize = parsedPool;
        }

        return NormalizeConnectionString(raw.Trim(), configuredMaxPoolSize);
    }

    /// <summary>
    /// Normalizes connection strings: parses postgres:// and postgresql:// URIs if provided,
    /// and configures connection pooling budget without altering explicitly user-supplied settings.
    /// </summary>
    public static string NormalizeConnectionString(string raw, int? defaultMaxPoolSize = null)
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

        // Check if caller explicitly supplied pool size in the connection string using Npgsql properties
        // instead of raw substring matching.
        var hasExplicitPoolSize = builder.ContainsKey("Maximum Pool Size") || builder.ContainsKey("MaxPoolSize");

        // Connection budget rationale:
        // PostgreSQL's default max_connections is 100 (97 usable by non-superusers).
        // For standard single-instance deployments, Npgsql's default MaxPoolSize (100) is preserved.
        // For multi-process deployments sharing one DB (e.g. 2 API processes + test runner),
        // each process should budget ~35 connections (35 + 35 + 15 = 85 <= 97) to prevent
        // 'sorry, too many clients already' connection exhaustion.
        // We only apply defaultMaxPoolSize if explicitly requested and the connection string did not specify one.
        if (!hasExplicitPoolSize && defaultMaxPoolSize.HasValue && defaultMaxPoolSize.Value > 0)
        {
            builder.MaxPoolSize = defaultMaxPoolSize.Value;
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

