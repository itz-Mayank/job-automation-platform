using JobForge.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace JobForge.Api.Middleware;

/// <summary>
/// Central place that turns exceptions into consistent ProblemDetails responses
/// (spec section 33) instead of leaking stack traces. Domain exceptions map to the
/// specific status they represent; anything else is logged with full detail
/// server-side and returned to the client as an opaque 500.
/// </summary>
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
            var (status, title) = Classify(ex);

            if (status == StatusCodes.Status500InternalServerError)
            {
                _logger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path);
            }
            else
            {
                _logger.LogInformation("{ExceptionType}: {Message}", ex.GetType().Name, ex.Message);
            }

            var problem = new ProblemDetails
            {
                Type = $"https://httpstatuses.io/{status}",
                Title = title,
                Status = status,
                Detail = status == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : ex.Message,
                Instance = context.Request.Path
            };
            problem.Extensions["traceId"] = context.TraceIdentifier;

            context.Response.StatusCode = status;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(problem);
        }
    }

    private static (int Status, string Title) Classify(Exception ex) => ex switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
        ValidationException => (StatusCodes.Status422UnprocessableEntity, "Validation Failed"),
        ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
        UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
        InvalidStateTransitionException => (StatusCodes.Status409Conflict, "Invalid State Transition"),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
    };
}
