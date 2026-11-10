using AdmissionSystem.Domain.Common;

namespace AdmissionSystem.Domain.Holds;

public sealed class AdmissionHold
{
    public Guid Id { get; private set; }
    public Guid ClassId { get; private set; }
    public string StudentId { get; private set; } = string.Empty;
    public HoldStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }

    private AdmissionHold()
    {
    }

    private AdmissionHold(
        Guid id,
        Guid classId,
        string studentId,
        HoldStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        Id = id;
        ClassId = classId;
        StudentId = studentId;
        Status = status;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
    }

    public static AdmissionHold Create(
        Guid classId,
        string studentId,
        DateTimeOffset nowUtc,
        TimeSpan duration)
    {
        if (classId == Guid.Empty)
        {
            throw new DomainValidationException(ErrorCodes.InvalidRequest, "Class ID cannot be empty.");
        }

        if (string.IsNullOrWhiteSpace(studentId) || studentId.Length != 17 || !studentId.All(char.IsAsciiDigit))
        {
            throw new DomainValidationException(
                ErrorCodes.InvalidStudentId,
                "Student ID must be an exact 17-digit birth registration number.");
        }

        if (duration <= TimeSpan.Zero)
        {
            throw new DomainValidationException(ErrorCodes.InvalidRequest, "Hold duration must be positive.");
        }

        return new AdmissionHold(
            Guid.NewGuid(),
            classId,
            studentId,
            HoldStatus.HELD,
            nowUtc,
            nowUtc.Add(duration));
    }

    public HoldStatus GetEffectiveStatus(DateTimeOffset nowUtc)
    {
        if (Status == HoldStatus.HELD && ExpiresAt <= nowUtc)
        {
            return HoldStatus.EXPIRED;
        }

        return Status;
    }

    /// <summary>
    /// Confirms the hold if owned by the student and still active.
    /// Returns true if state changed from HELD to CONFIRMED, or false if already CONFIRMED (idempotent).
    /// </summary>
    public bool Confirm(string requestingStudentId, DateTimeOffset nowUtc)
    {
        EnsureOwnership(requestingStudentId);

        if (Status == HoldStatus.CONFIRMED)
        {
            return false;
        }

        if (Status == HoldStatus.CANCELLED)
        {
            throw new HoldConflictException(
                ErrorCodes.HoldCancelled,
                "This application has already been cancelled and cannot be confirmed.");
        }

        if (Status == HoldStatus.EXPIRED || ExpiresAt <= nowUtc)
        {
            Status = HoldStatus.EXPIRED;
            throw new HoldConflictException(
                ErrorCodes.HoldExpired,
                "This seat hold has expired and can no longer be confirmed.");
        }

        Status = HoldStatus.CONFIRMED;
        ConfirmedAt = nowUtc;
        return true;
    }

    public void Cancel(string requestingStudentId, DateTimeOffset nowUtc)
    {
        EnsureOwnership(requestingStudentId);

        if (Status == HoldStatus.CONFIRMED)
        {
            throw new HoldConflictException(
                ErrorCodes.HoldAlreadyConfirmed,
                "A confirmed admission cannot be cancelled.");
        }

        if (Status == HoldStatus.CANCELLED)
        {
            throw new HoldConflictException(
                ErrorCodes.HoldCancelled,
                "This application is already cancelled.");
        }

        if (Status == HoldStatus.EXPIRED || ExpiresAt <= nowUtc)
        {
            Status = HoldStatus.EXPIRED;
            throw new HoldConflictException(
                ErrorCodes.HoldExpired,
                "This seat hold has already expired.");
        }

        Status = HoldStatus.CANCELLED;
        CancelledAt = nowUtc;
    }

    public void MarkExpiredIfOverdue(DateTimeOffset nowUtc)
    {
        if (Status == HoldStatus.HELD && ExpiresAt <= nowUtc)
        {
            Status = HoldStatus.EXPIRED;
        }
    }

    private void EnsureOwnership(string requestingStudentId)
    {
        if (!string.Equals(StudentId, requestingStudentId, StringComparison.Ordinal))
        {
            throw new HoldOwnershipException("You can only manage your own admission application.");
        }
    }
}

