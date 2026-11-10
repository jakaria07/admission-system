using System.Data;
using AdmissionSystem.Application.Common;
using AdmissionSystem.Application.Holds;
using AdmissionSystem.Domain.Classes;
using AdmissionSystem.Domain.Common;
using AdmissionSystem.Domain.Holds;
using AdmissionSystem.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AdmissionSystem.Infrastructure.Repositories;

public sealed class HoldRepository : IHoldRepository
{
    private readonly AdmissionDbContext _dbContext;
    private readonly IClock _clock;

    public HoldRepository(AdmissionDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<HoldCreatedResponseDto> CreateHoldUnderClassLockAsync(
        Guid classId,
        StudentId studentId,
        TimeSpan holdDuration,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // 1. Acquire pessimistic row-level lock on the target class row first (consistent lock order across all write flows).
        var lockedClass = await _dbContext.AdmissionClasses
            .FromSqlInterpolated($"SELECT id, name, name_normalized, total_seats, created_at FROM admission_classes WHERE id = {classId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (lockedClass is null)
        {
            throw new NotFoundException(ErrorCodes.ClassNotFound, $"Class '{classId}' was not found.");
        }

        // 2. Read real-time wall clock AFTER acquiring the row lock (never use transaction-frozen now()).
        var nowUtc = await _clock.GetClockTimestampAsync(cancellationToken);

        // 3. Flip any overdue HELD rows for this class to EXPIRED inside the same locked transaction
        //    so seats return immediately and partial unique index is freed for re-applying students.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE admission_holds SET status = 'EXPIRED' WHERE class_id = {classId} AND status = 'HELD' AND expires_at <= {nowUtc}",
            cancellationToken);

        // 4. Enforce Rule 3: a student cannot have more than one active (HELD) or confirmed (CONFIRMED) application in the same class.
        var hasExistingActiveOrConfirmed = await _dbContext.AdmissionHolds
            .AnyAsync(h =>
                h.ClassId == classId &&
                h.StudentId == studentId.Value &&
                ((h.Status == HoldStatus.HELD && h.ExpiresAt > nowUtc) || h.Status == HoldStatus.CONFIRMED),
                cancellationToken);

        if (hasExistingActiveOrConfirmed)
        {
            throw new ConflictException(
                ErrorCodes.DuplicateApplication,
                "This student already has an active or confirmed application in this class.");
        }

        // 5. Enforce Rule 1: held + confirmed must never exceed totalSeats.
        var occupiedSeats = await _dbContext.AdmissionHolds
            .CountAsync(h =>
                h.ClassId == classId &&
                ((h.Status == HoldStatus.HELD && h.ExpiresAt > nowUtc) || h.Status == HoldStatus.CONFIRMED),
                cancellationToken);

        if (occupiedSeats >= lockedClass.TotalSeats)
        {
            throw new ConflictException(
                ErrorCodes.NoSeats,
                "No seats are currently available in this class.");
        }

        // 6. Create and persist the new hold with expires_at = clock_timestamp() + duration.
        var hold = AdmissionHold.Create(classId, studentId.Value, nowUtc, holdDuration);
        _dbContext.AdmissionHolds.Add(hold);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException(
                ErrorCodes.DuplicateApplication,
                "This student already has an active or confirmed application in this class.");
        }

        return new HoldCreatedResponseDto(
            HoldId: hold.Id.ToString(),
            ClassId: hold.ClassId.ToString(),
            StudentId: hold.StudentId,
            Status: hold.Status.ToString(),
            ExpiresAt: FormatUtcIso(hold.ExpiresAt));
    }

    public async Task<HoldDetailsResponseDto?> GetHoldDetailsByIdAsync(
        Guid holdId,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = await _clock.GetDatabaseNowAsync(cancellationToken);

        var hold = await _dbContext.AdmissionHolds
            .AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == holdId, cancellationToken);

        if (hold is null)
        {
            return null;
        }

        var effectiveStatus = hold.GetEffectiveStatus(nowUtc);

        return new HoldDetailsResponseDto(
            HoldId: hold.Id.ToString(),
            ClassId: hold.ClassId.ToString(),
            Status: effectiveStatus.ToString(),
            ExpiresAt: FormatUtcIso(hold.ExpiresAt));
    }

    public async Task<HoldConfirmedResponseDto> ConfirmHoldUnderClassLockAsync(
        Guid holdId,
        StudentId studentId,
        CancellationToken cancellationToken = default)
    {
        // 1. Read only the immutable class_id first (safe without lock; 404 if hold does not exist).
        var targetClassId = await _dbContext.AdmissionHolds
            .AsNoTracking()
            .Where(h => h.Id == holdId)
            .Select(h => (Guid?)h.ClassId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!targetClassId.HasValue)
        {
            throw new NotFoundException(ErrorCodes.HoldNotFound, $"Hold '{holdId}' was not found.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // 2. Lock the parent class row first to maintain global lock order (Class -> Hold) and prevent deadlocks.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM admission_classes WHERE id = {targetClassId.Value} FOR UPDATE",
            cancellationToken);

        // 3. Re-read the hold row FOR UPDATE inside the transaction (never use a pre-lock copy).
        var hold = await _dbContext.AdmissionHolds
            .FromSqlInterpolated($"SELECT id, class_id, student_id, status, created_at, expires_at, confirmed_at, cancelled_at FROM admission_holds WHERE id = {holdId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (hold is null)
        {
            throw new NotFoundException(ErrorCodes.HoldNotFound, $"Hold '{holdId}' was not found.");
        }

        // 4. Evaluate real-time clock_timestamp() AFTER locks are acquired.
        var nowUtc = await _clock.GetClockTimestampAsync(cancellationToken);

        try
        {
            var stateChanged = hold.Confirm(studentId.Value, nowUtc);
            if (stateChanged)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (HoldConflictException ex) when (ex.Code == ErrorCodes.HoldExpired)
        {
            // Persist the EXPIRED transition before rethrowing the 409 conflict.
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw;
        }

        return new HoldConfirmedResponseDto(
            HoldId: hold.Id.ToString(),
            Status: HoldStatus.CONFIRMED.ToString());
    }

    public async Task CancelHoldUnderClassLockAsync(
        Guid holdId,
        StudentId studentId,
        CancellationToken cancellationToken = default)
    {
        // 1. Read immutable class_id first (404 if hold does not exist).
        var targetClassId = await _dbContext.AdmissionHolds
            .AsNoTracking()
            .Where(h => h.Id == holdId)
            .Select(h => (Guid?)h.ClassId)
            .FirstOrDefaultAsync(cancellationToken);

        if (!targetClassId.HasValue)
        {
            throw new NotFoundException(ErrorCodes.HoldNotFound, $"Hold '{holdId}' was not found.");
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);

        // 2. Lock parent class row first (Class -> Hold lock order).
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM admission_classes WHERE id = {targetClassId.Value} FOR UPDATE",
            cancellationToken);

        // 3. Re-read the hold row FOR UPDATE inside the transaction.
        var hold = await _dbContext.AdmissionHolds
            .FromSqlInterpolated($"SELECT id, class_id, student_id, status, created_at, expires_at, confirmed_at, cancelled_at FROM admission_holds WHERE id = {holdId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

        if (hold is null)
        {
            throw new NotFoundException(ErrorCodes.HoldNotFound, $"Hold '{holdId}' was not found.");
        }

        // 4. Evaluate real-time clock_timestamp() AFTER locks are acquired.
        var nowUtc = await _clock.GetClockTimestampAsync(cancellationToken);

        try
        {
            hold.Cancel(studentId.Value, nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (HoldConflictException ex) when (ex.Code == ErrorCodes.HoldExpired)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            throw;
        }
    }

    public async Task<List<MyHoldItemResponseDto>> GetHoldsByStudentAsync(
        StudentId studentId,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = await _clock.GetDatabaseNowAsync(cancellationToken);

        var rows = await (
            from h in _dbContext.AdmissionHolds.AsNoTracking()
            join c in _dbContext.AdmissionClasses.AsNoTracking() on h.ClassId equals c.Id
            where h.StudentId == studentId.Value
            orderby h.CreatedAt descending
            select new
            {
                h.Id,
                h.ClassId,
                ClassName = c.Name,
                h.Status,
                h.ExpiresAt
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r =>
            {
                var effectiveStatus = r.Status == HoldStatus.HELD && r.ExpiresAt <= nowUtc
                    ? HoldStatus.EXPIRED
                    : r.Status;

                return new MyHoldItemResponseDto(
                    HoldId: r.Id.ToString(),
                    ClassId: r.ClassId.ToString(),
                    ClassName: r.ClassName,
                    Status: effectiveStatus.ToString(),
                    ExpiresAt: FormatUtcIso(r.ExpiresAt));
            })
            .ToList();
    }

    public async Task<int> ExpireOverdueHoldsBatchAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Database.ExecuteSqlRawAsync(
            "UPDATE admission_holds SET status = 'EXPIRED' WHERE status = 'HELD' AND expires_at <= now()",
            cancellationToken);
    }

    private static string FormatUtcIso(DateTimeOffset timestamp)
    {
        return timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
    }
}

