using FluentValidation;

namespace TodoApi.Infrastructure;

/// <summary>
/// Runs the registered <see cref="IValidator{T}"/> against the request body before the
/// handler sees it, and short-circuits with a 400 problem details response if it fails.
/// </summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
    where T : class
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        // The body is the only argument of this type on these routes; if binding failed
        // the request never reaches here (BadRequestExceptionHandler turns that into a 400).
        var argument = context.Arguments.OfType<T>().FirstOrDefault();
        if (argument is null)
        {
            return await next(context);
        }

        var result = await validator.ValidateAsync(argument, context.HttpContext.RequestAborted);
        if (result.IsValid)
        {
            return await next(context);
        }

        var errors = result.Errors
            .GroupBy(failure => ToJsonName(failure.PropertyName))
            .ToDictionary(group => group.Key, group => group.Select(f => f.ErrorMessage).ToArray());

        return TypedResults.ValidationProblem(errors);
    }

    /// <summary>
    /// FluentValidation reports CLR property names ("Title"); the API's error keys are the
    /// JSON names ("title"), including for nested paths such as "Items[0].Title".
    /// </summary>
    private static string ToJsonName(string propertyName) =>
        string.Join('.', propertyName.Split('.').Select(Camelize));

    private static string Camelize(string segment) =>
        segment.Length == 0 || char.IsLower(segment[0])
            ? segment
            : char.ToLowerInvariant(segment[0]) + segment[1..];
}

public static class ValidationFilterExtensions
{
    /// <summary>
    /// Validates the <typeparamref name="T"/> body of this route and documents the 400 it
    /// can produce.
    /// </summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder)
        where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
