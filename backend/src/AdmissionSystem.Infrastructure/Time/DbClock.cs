using AdmissionSystem.Application.Common;
using AdmissionSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AdmissionSystem.Infrastructure.Time;

public sealed class DbClock : IClock
{
    private readonly AdmissionDbContext _dbContext;

    public DbClock(AdmissionDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DateTimeOffset> GetDatabaseNowAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Database
            .SqlQueryRaw<DateTimeOffset>("SELECT now() AS \"Value\"")
            .SingleAsync(cancellationToken);
    }

    public async Task<DateTimeOffset> GetClockTimestampAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Database
            .SqlQueryRaw<DateTimeOffset>("SELECT clock_timestamp() AS \"Value\"")
            .SingleAsync(cancellationToken);
    }
}

