namespace AdmissionSystem.Application.Holds;

public sealed record HoldCreatedResponseDto(
    string HoldId,
    string ClassId,
    string StudentId,
    string Status,
    string ExpiresAt);

public sealed record HoldDetailsResponseDto(
    string HoldId,
    string ClassId,
    string Status,
    string ExpiresAt);

public sealed record HoldConfirmedResponseDto(
    string HoldId,
    string Status);

public sealed record MyHoldItemResponseDto(
    string HoldId,
    string ClassId,
    string ClassName,
    string Status,
    string ExpiresAt);

