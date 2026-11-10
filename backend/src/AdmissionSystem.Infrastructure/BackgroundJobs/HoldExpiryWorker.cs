using AdmissionSystem.Application.Holds;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AdmissionSystem.Infrastructure.BackgroundJobs;

public sealed class HoldExpiryWorker : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(2);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HoldExpiryWorker> _logger;

    public HoldExpiryWorker(IServiceScopeFactory scopeFactory, ILogger<HoldExpiryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var holdRepository = scope.ServiceProvider.GetRequiredService<IHoldRepository>();
                var expiredCount = await holdRepository.ExpireOverdueHoldsBatchAsync(stoppingToken);
                if (expiredCount > 0)
                {
                    _logger.LogInformation("Marked {ExpiredCount} overdue seat holds as EXPIRED.", expiredCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error occurred while sweeping overdue seat holds.");
            }
        }
    }
}

