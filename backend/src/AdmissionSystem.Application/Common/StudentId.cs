using AdmissionSystem.Domain.Common;

namespace AdmissionSystem.Application.Common;

public readonly record struct StudentId
{
    public const int RequiredLength = 17;
    public string Value { get; }

    private StudentId(string value)
    {
        Value = value;
    }

    public static StudentId Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainValidationException(
                ErrorCodes.MissingStudentId,
                "Header 'X-Student-Id' is required.");
        }

        var trimmed = raw.Trim();
        if (trimmed.Length != RequiredLength || !trimmed.All(char.IsAsciiDigit))
        {
            throw new DomainValidationException(
                ErrorCodes.InvalidStudentId,
                "Student ID ('X-Student-Id') must be an exact 17-digit birth registration number.");
        }

        return new StudentId(trimmed);
    }

    public override string ToString() => Value;
}

