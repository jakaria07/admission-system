using AdmissionSystem.Application.Classes;
using AdmissionSystem.Application.Common;
using AdmissionSystem.Domain.Classes;
using AdmissionSystem.Domain.Common;
using AdmissionSystem.Domain.Holds;
using AdmissionSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AdmissionSystem.Infrastructure.Repositories;

public sealed class ClassRepository : IClassRepository
{
    private readonly AdmissionDbContext _dbContext;
    private readonly IClock _clock;

    public ClassRepository(AdmissionDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task AddAsync(AdmissionClass admissionClass, CancellationToken cancellationToken = default)
    {
        _dbContext.AdmissionClasses.Add(admissionClass);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException(
                ErrorCodes.DuplicateClassName,
                $"A class with name '{admissionClass.Name}' already exists.");
        }
    }

    public async Task<List<ClassSummaryResponseDto>> GetAllWithSeatCountsAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = await _clock.GetDatabaseNowAsync(cancellationToken);

        var rows = await _dbContext.AdmissionClasses
            .AsNoTracking()
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Name)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.TotalSeats,
                HeldSeats = _dbContext.AdmissionHolds.Count(h =>
                    h.ClassId == c.Id &&
                    h.Status == HoldStatus.HELD &&
                    h.ExpiresAt > nowUtc),
                ConfirmedSeats = _dbContext.AdmissionHolds.Count(h =>
                    h.ClassId == c.Id &&
                    h.Status == HoldStatus.CONFIRMED)
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new ClassSummaryResponseDto(
                Id: r.Id.ToString(),
                Name: r.Name,
                TotalSeats: r.TotalSeats,
                HeldSeats: r.HeldSeats,
                ConfirmedSeats: r.ConfirmedSeats,
                AvailableSeats: Math.Max(0, r.TotalSeats - r.HeldSeats - r.ConfirmedSeats)))
            .ToList();
    }

    public async Task<ClassSummaryResponseDto?> GetByIdWithSeatCountsAsync(
        Guid classId,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = await _clock.GetDatabaseNowAsync(cancellationToken);

        var row = await _dbContext.AdmissionClasses
            .AsNoTracking()
            .Where(c => c.Id == classId)
            .Select(c => new
            {
                c.Id,
                c.Name,
                c.TotalSeats,
                HeldSeats = _dbContext.AdmissionHolds.Count(h =>
                    h.ClassId == c.Id &&
                    h.Status == HoldStatus.HELD &&
                    h.ExpiresAt > nowUtc),
                ConfirmedSeats = _dbContext.AdmissionHolds.Count(h =>
                    h.ClassId == c.Id &&
                    h.Status == HoldStatus.CONFIRMED)
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            return null;
        }

        return new ClassSummaryResponseDto(
            Id: row.Id.ToString(),
            Name: row.Name,
            TotalSeats: row.TotalSeats,
            HeldSeats: row.HeldSeats,
            ConfirmedSeats: row.ConfirmedSeats,
            AvailableSeats: Math.Max(0, row.TotalSeats - row.HeldSeats - row.ConfirmedSeats));
    }
}

