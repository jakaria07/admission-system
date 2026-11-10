using AdmissionSystem.Application.Common;
using AdmissionSystem.Domain.Common;

namespace AdmissionSystem.Api.Filters;

public static class HttpRequestExtensions
{
    public const string StudentIdHeaderName = "X-Student-Id";

    public static StudentId RequireStudentId(this HttpRequest request)
    {
        if (!request.Headers.TryGetValue(StudentIdHeaderName, out var values) || values.Count == 0)
        {
            throw new DomainValidationException(
                ErrorCodes.MissingStudentId,
                $"Header '{StudentIdHeaderName}' is required.");
        }

        return StudentId.Parse(values.ToString());
    }

    public static Guid ParseGuidId(string rawId, string notFoundCode, string resourceLabel)
    {
        if (!Guid.TryParse(rawId, out var parsed) || parsed == Guid.Empty)
        {
            throw new NotFoundException(notFoundCode, $"{resourceLabel} '{rawId}' was not found.");
        }

        return parsed;
    }
}

