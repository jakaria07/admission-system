using AdmissionSystem.Application.Common;

namespace AdmissionSystem.Application.Holds;

public interface IHoldRepository
{
    Task<HoldCreatedResponseDto> CreateHoldUnderClassLockAsync(
        Guid classId,
        StudentId studentId,
        TimeSpan holdDuration,
        CancellationToken cancellationToken = default);

    Task<HoldDetailsResponseDto?> GetHoldDetailsByIdAsync(
        Guid holdId,
        CancellationToken cancellationToken = default);

    Task<HoldConfirmedResponseDto> ConfirmHoldUnderClassLockAsync(
        Guid holdId,
        StudentId studentId,
        CancellationToken cancellationToken = default);

    Task CancelHoldUnderClassLockAsync(
        Guid holdId,
        StudentId studentId,
        CancellationToken cancellationToken = default);

    Task<List<MyHoldItemResponseDto>> GetHoldsByStudentAsync(
        StudentId studentId,
        CancellationToken cancellationToken = default);

    Task<int> ExpireOverdueHoldsBatchAsync(CancellationToken cancellationToken = default);
}

