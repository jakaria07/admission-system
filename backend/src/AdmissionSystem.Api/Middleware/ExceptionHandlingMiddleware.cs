using AdmissionSystem.Application.Common;
using AdmissionSystem.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace AdmissionSystem.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title, code, detail) = exception switch
        {
            DomainValidationException valEx => (
                StatusCodes.Status400BadRequest,
                "Validation Failed",
                valEx.Code,
                valEx.Message),

            HoldOwnershipException ownEx => (
                StatusCodes.Status403Forbidden,
                "Forbidden",
                ownEx.Code,
                ownEx.Message),

            NotFoundException notFoundEx => (
                StatusCodes.Status404NotFound,
                "Resource Not Found",
                notFoundEx.Code,
                notFoundEx.Message),

            HoldConflictException holdConflictEx => (
                StatusCodes.Status409Conflict,
                "Application Conflict",
                holdConflictEx.Code,
                holdConflictEx.Message),

            ConflictException conflictEx => (
                StatusCodes.Status409Conflict,
                "Resource Conflict",
                conflictEx.Code,
                conflictEx.Message),

            BadHttpRequestException badReqEx => (
                StatusCodes.Status400BadRequest,
                "Bad Request",
                ErrorCodes.InvalidRequest,
                badReqEx.Message),

            _ => (
                StatusCodes.Status500InternalServerError,
                "Internal Server Error",
                "INTERNAL_SERVER_ERROR",
                "An unexpected server error occurred.")
        };

        if (statusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
        }

        if (context.Response.HasStarted)
        {
            return;
        }

        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = context.Request.Path
        };
        problem.Extensions["code"] = code;

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}

