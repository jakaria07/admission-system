using System.Text.Json;
using AdmissionSystem.Api.Middleware;
using AdmissionSystem.Application;
using AdmissionSystem.Domain.Common;
using AdmissionSystem.Infrastructure;
using AdmissionSystem.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.SuppressMapClientErrors = true;
        options.InvalidModelStateResponseFactory = context =>
        {
            var firstError = context.ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage)
                .FirstOrDefault(msg => !string.IsNullOrWhiteSpace(msg))
                ?? "The request payload is invalid or malformed.";

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid Request",
                Detail = firstError,
                Instance = context.HttpContext.Request.Path
            };
            problem.Extensions["code"] = ErrorCodes.InvalidRequest;

            return new BadRequestObjectResult(problem)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });

var allowedOrigins = builder.Configuration["CORS_ALLOWED_ORIGINS"]
    ?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        if (allowedOrigins is { Length: > 0 })
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

var app = builder.Build();

await ApplyDatabaseMigrationsAsync(app);

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors();
app.MapControllers();

app.MapGet("/health", async (AdmissionDbContext dbContext, CancellationToken cancellationToken) =>
{
    var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "Healthy" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.Run();

static async Task ApplyDatabaseMigrationsAsync(WebApplication app)
{
    if (string.Equals(app.Configuration["SKIP_MIGRATIONS"], "true", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    const long migrationAdvisoryLockKey = 7283940129384712L;

    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseMigration");

    await dbContext.Database.OpenConnectionAsync();
    try
    {
        await dbContext.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_lock({migrationAdvisoryLockKey});");
        try
        {
            await dbContext.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied successfully.");
        }
        finally
        {
            await dbContext.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({migrationAdvisoryLockKey});");
        }
    }
    finally
    {
        await dbContext.Database.CloseConnectionAsync();
    }
}

public partial class Program;
