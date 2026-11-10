using AdmissionSystem.Application.Common;
using AdmissionSystem.Domain.Classes;
using AdmissionSystem.Domain.Common;

namespace AdmissionSystem.Application.Classes;

public sealed class ClassAppService
{
    private readonly IClassRepository _classRepository;
    private readonly IClock _clock;

    public ClassAppService(IClassRepository classRepository, IClock clock)
    {
        _classRepository = classRepository;
        _clock = clock;
    }

    public async Task<ClassSummaryResponseDto> CreateClassAsync(
        CreateClassRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            throw new DomainValidationException(ErrorCodes.InvalidRequest, "Request body is required.");
        }

        if (!request.TotalSeats.HasValue)
        {
            throw new DomainValidationException(ErrorCodes.InvalidTotalSeats, "TotalSeats is required.");
        }

        var nowUtc = await _clock.GetDatabaseNowAsync(cancellationToken);
        var admissionClass = AdmissionClass.Create(request.Name, request.TotalSeats.Value, nowUtc);

        await _classRepository.AddAsync(admissionClass, cancellationToken);

        return new ClassSummaryResponseDto(
            Id: admissionClass.Id.ToString(),
            Name: admissionClass.Name,
            TotalSeats: admissionClass.TotalSeats,
            HeldSeats: 0,
            ConfirmedSeats: 0,
            AvailableSeats: admissionClass.TotalSeats);
    }

    public Task<List<ClassSummaryResponseDto>> GetAllClassesAsync(CancellationToken cancellationToken = default)
    {
        return _classRepository.GetAllWithSeatCountsAsync(cancellationToken);
    }

    public async Task<ClassSummaryResponseDto> GetClassByIdAsync(
        Guid classId,
        CancellationToken cancellationToken = default)
    {
        var summary = await _classRepository.GetByIdWithSeatCountsAsync(classId, cancellationToken);
        if (summary is null)
        {
            throw new NotFoundException(ErrorCodes.ClassNotFound, $"Class '{classId}' was not found.");
        }

        return summary;
    }
}

