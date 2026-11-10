namespace AdmissionSystem.Application.Common;

public sealed class HoldOptions
{
    public const string SectionName = "HoldSettings";

    public int HoldDurationSeconds { get; set; } = 600;
}

