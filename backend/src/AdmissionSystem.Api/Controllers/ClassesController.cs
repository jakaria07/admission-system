using AdmissionSystem.Api.Filters;
using AdmissionSystem.Application.Classes;
using AdmissionSystem.Application.Holds;
using AdmissionSystem.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace AdmissionSystem.Api.Controllers;

[ApiController]
[Route("classes")]
public sealed class ClassesController : ControllerBase
{
    private readonly ClassAppService _classAppService;
    private readonly HoldAppService _holdAppService;

    public ClassesController(ClassAppService classAppService, HoldAppService holdAppService)
    {
        _classAppService = classAppService;
        _holdAppService = holdAppService;
    }

    [HttpPost]
    public async Task<IActionResult> CreateClass(
        [FromBody] CreateClassRequestDto? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new DomainValidationException(ErrorCodes.InvalidRequest, "Request body is required.");
        }

        var created = await _classAppService.CreateClassAsync(request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [HttpGet]
    public async Task<IActionResult> GetClasses(CancellationToken cancellationToken)
    {
        var classes = await _classAppService.GetAllClassesAsync(cancellationToken);
        return Ok(classes);
    }

    [HttpGet("{classId}")]
    public async Task<IActionResult> GetClassById(
        [FromRoute] string classId,
        CancellationToken cancellationToken)
    {
        var parsedClassId = HttpRequestExtensions.ParseGuidId(classId, ErrorCodes.ClassNotFound, "Class");
        var summary = await _classAppService.GetClassByIdAsync(parsedClassId, cancellationToken);
        return Ok(summary);
    }

    [HttpPost("{classId}/holds")]
    public async Task<IActionResult> CreateHold(
        [FromRoute] string classId,
        CancellationToken cancellationToken)
    {
        var studentId = Request.RequireStudentId();
        var parsedClassId = HttpRequestExtensions.ParseGuidId(classId, ErrorCodes.ClassNotFound, "Class");

        var createdHold = await _holdAppService.CreateHoldAsync(parsedClassId, studentId, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, createdHold);
    }
}

