using AdmissionSystem.Application.Common;
using AdmissionSystem.Domain.Common;

namespace AdmissionSystem.Application.Holds;

public sealed class HoldAppService
{
    private readonly IHoldRepository _holdRepository;
    private readonly HoldOptions _holdOptions;

    public HoldAppService(IHoldRepository holdRepository, HoldOptions holdOptions)
    {
        _holdRepository = holdRepository;
        _holdOptions = holdOptions;
    }

    public Task<HoldCreatedResponseDto> CreateHoldAsync(
        Guid classId,
        StudentId studentId,
        CancellationToken cancellationToken = default)
    {
        var durationSeconds = _holdOptions.HoldDurationSeconds > 0
            ? _holdOptions.HoldDurationSeconds
            : 600;

        return _holdRepository.CreateHoldUnderClassLockAsync(
            classId,
            studentId,
            TimeSpan.FromSeconds(durationSeconds),
            cancellationToken);
    }

    public async Task<HoldDetailsResponseDto> GetHoldByIdAsync(
        Guid holdId,
        CancellationToken cancellationToken = default)
    {
        var hold = await _holdRepository.GetHoldDetailsByIdAsync(holdId, cancellationToken);
        if (hold is null)
        {
            throw new NotFoundException(ErrorCodes.HoldNotFound, $"Hold '{holdId}' was not found.");
        }

        return hold;
    }

    public Task<HoldConfirmedResponseDto> ConfirmHoldAsync(
        Guid holdId,
        StudentId studentId,
        CancellationToken cancellationToken = default)
    {
        return _holdRepository.ConfirmHoldUnderClassLockAsync(holdId, studentId, cancellationToken);
    }

    public Task CancelHoldAsync(
        Guid holdId,
        StudentId studentId,
        CancellationToken cancellationToken = default)
    {
        return _holdRepository.CancelHoldUnderClassLockAsync(holdId, studentId, cancellationToken);
    }

    public Task<List<MyHoldItemResponseDto>> GetMyHoldsAsync(
        StudentId studentId,
        CancellationToken cancellationToken = default)
    {
        return _holdRepository.GetHoldsByStudentAsync(studentId, cancellationToken);
    }
}

