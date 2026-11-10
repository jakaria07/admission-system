using AdmissionSystem.Domain.Classes;

namespace AdmissionSystem.Application.Classes;

public interface IClassRepository
{
    Task AddAsync(AdmissionClass admissionClass, CancellationToken cancellationToken = default);
    Task<List<ClassSummaryResponseDto>> GetAllWithSeatCountsAsync(CancellationToken cancellationToken = default);
    Task<ClassSummaryResponseDto?> GetByIdWithSeatCountsAsync(Guid classId, CancellationToken cancellationToken = default);
}

