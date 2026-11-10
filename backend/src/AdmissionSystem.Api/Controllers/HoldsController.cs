using AdmissionSystem.Api.Filters;
using AdmissionSystem.Application.Holds;
using AdmissionSystem.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace AdmissionSystem.Api.Controllers;

[ApiController]
[Route("holds")]
public sealed class HoldsController : ControllerBase
{
    private readonly HoldAppService _holdAppService;

    public HoldsController(HoldAppService holdAppService)
    {
        _holdAppService = holdAppService;
    }

    [HttpGet("{holdId}")]
    public async Task<IActionResult> GetHoldById(
        [FromRoute] string holdId,
        CancellationToken cancellationToken)
    {
        var parsedHoldId = HttpRequestExtensions.ParseGuidId(holdId, ErrorCodes.HoldNotFound, "Hold");
        var hold = await _holdAppService.GetHoldByIdAsync(parsedHoldId, cancellationToken);
        return Ok(hold);
    }

    [HttpPost("{holdId}/confirm")]
    public async Task<IActionResult> ConfirmHold(
        [FromRoute] string holdId,
        CancellationToken cancellationToken)
    {
        var studentId = Request.RequireStudentId();
        var parsedHoldId = HttpRequestExtensions.ParseGuidId(holdId, ErrorCodes.HoldNotFound, "Hold");

        var confirmed = await _holdAppService.ConfirmHoldAsync(parsedHoldId, studentId, cancellationToken);
        return Ok(confirmed);
    }

    [HttpDelete("{holdId}")]
    public async Task<IActionResult> CancelHold(
        [FromRoute] string holdId,
        CancellationToken cancellationToken)
    {
        var studentId = Request.RequireStudentId();
        var parsedHoldId = HttpRequestExtensions.ParseGuidId(holdId, ErrorCodes.HoldNotFound, "Hold");

        await _holdAppService.CancelHoldAsync(parsedHoldId, studentId, cancellationToken);
        return NoContent();
    }
}

