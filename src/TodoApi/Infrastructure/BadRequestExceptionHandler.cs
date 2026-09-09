using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace TodoApi.Infrastructure;

/// <summary>
/// Turns an unreadable request body - malformed JSON, or an unknown status or
/// priority name such as "Urgent" - into a 400 with problem details rather than
/// letting it surface as a 500.
/// </summary>
public sealed class BadRequestExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, detail) = Describe(exception);
        if (statusCode is null)
        {
            // Not a client error - let the default handler log and return a 500.
            return false;
        }

        httpContext.Response.StatusCode = statusCode.Value;

        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = statusCode,
                Title = "Invalid request",
                Detail = detail
            }
        });
    }

    private static (int? StatusCode, string? Detail) Describe(Exception exception) => exception switch
    {
        JsonException json => (StatusCodes.Status400BadRequest, json.Message),
        BadHttpRequestException bad => (bad.StatusCode, bad.Message),
        { InnerException: { } inner } => Describe(inner),
        _ => (null, null)
    };
}
