namespace AdmissionSystem.Application.Classes;

public sealed record CreateClassRequestDto(
    string? Name,
    int? TotalSeats);

public sealed record ClassSummaryResponseDto(
    string Id,
    string Name,
    int TotalSeats,
    int HeldSeats,
    int ConfirmedSeats,
    int AvailableSeats);

