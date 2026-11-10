using AdmissionSystem.Api.Filters;
using AdmissionSystem.Application.Holds;
using Microsoft.AspNetCore.Mvc;

namespace AdmissionSystem.Api.Controllers;

[ApiController]
[Route("me")]
public sealed class MeController : ControllerBase
{
    private readonly HoldAppService _holdAppService;

    public MeController(HoldAppService holdAppService)
    {
        _holdAppService = holdAppService;
    }

    [HttpGet("holds")]
    public async Task<IActionResult> GetMyHolds(CancellationToken cancellationToken)
    {
        var studentId = Request.RequireStudentId();
        var holds = await _holdAppService.GetMyHoldsAsync(studentId, cancellationToken);
        return Ok(holds);
    }
}

