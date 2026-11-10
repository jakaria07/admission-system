namespace AdmissionSystem.Domain.Common;

public abstract class DomainException : Exception
{
    public string Code { get; }

    protected DomainException(string code, string message) : base(message)
    {
        Code = code;
    }
}

public sealed class DomainValidationException : DomainException
{
    public DomainValidationException(string code, string message) : base(code, message)
    {
    }
}

public sealed class HoldConflictException : DomainException
{
    public HoldConflictException(string code, string message) : base(code, message)
    {
    }
}

public sealed class HoldOwnershipException : DomainException
{
    public HoldOwnershipException(string message) : base("FORBIDDEN_NOT_OWNER", message)
    {
    }
}

public static class ErrorCodes
{
    public const string InvalidClassName = "INVALID_CLASS_NAME";
    public const string InvalidTotalSeats = "INVALID_TOTAL_SEATS";
    public const string DuplicateClassName = "DUPLICATE_CLASS_NAME";
    public const string ClassNotFound = "CLASS_NOT_FOUND";
    public const string HoldNotFound = "HOLD_NOT_FOUND";
    public const string InvalidStudentId = "INVALID_STUDENT_ID";
    public const string MissingStudentId = "MISSING_STUDENT_ID";
    public const string NoSeats = "NO_SEATS";
    public const string DuplicateApplication = "DUPLICATE_APPLICATION";
    public const string HoldExpired = "HOLD_EXPIRED";
    public const string HoldCancelled = "HOLD_CANCELLED";
    public const string HoldAlreadyConfirmed = "HOLD_ALREADY_CONFIRMED";
    public const string ForbiddenNotOwner = "FORBIDDEN_NOT_OWNER";
    public const string InvalidRequest = "INVALID_REQUEST";
}

