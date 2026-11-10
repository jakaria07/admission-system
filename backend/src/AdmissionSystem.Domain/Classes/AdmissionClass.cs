using AdmissionSystem.Domain.Common;

namespace AdmissionSystem.Domain.Classes;

public sealed class AdmissionClass
{
    public const int MaxNameLength = 150;
    public const int MinTotalSeats = 1;
    public const int MaxTotalSeats = 10_000;

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NameNormalized { get; private set; } = string.Empty;
    public int TotalSeats { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private AdmissionClass()
    {
    }

    private AdmissionClass(Guid id, string name, string nameNormalized, int totalSeats, DateTimeOffset createdAt)
    {
        Id = id;
        Name = name;
        NameNormalized = nameNormalized;
        TotalSeats = totalSeats;
        CreatedAt = createdAt;
    }

    public static AdmissionClass Create(string? name, int totalSeats, DateTimeOffset nowUtc)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            throw new DomainValidationException(
                ErrorCodes.InvalidClassName,
                "Class name is required and cannot be empty.");
        }

        if (trimmedName.Length > MaxNameLength)
        {
            throw new DomainValidationException(
                ErrorCodes.InvalidClassName,
                $"Class name cannot exceed {MaxNameLength} characters.");
        }

        if (totalSeats < MinTotalSeats || totalSeats > MaxTotalSeats)
        {
            throw new DomainValidationException(
                ErrorCodes.InvalidTotalSeats,
                $"Total seats must be between {MinTotalSeats} and {MaxTotalSeats}.");
        }

        return new AdmissionClass(
            Guid.NewGuid(),
            trimmedName,
            NormalizeName(trimmedName),
            totalSeats,
            nowUtc);
    }

    public static string NormalizeName(string name) => name.Trim().ToUpperInvariant();
}

