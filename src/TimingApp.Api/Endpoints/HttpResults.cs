using TimingApp.Domain.SharedKernel;

namespace TimingApp.Api.Endpoints;

/// <summary>Maps application results to HTTP; failures become RFC 9457 ProblemDetails with <c>code</c> and <c>params</c>.</summary>
internal static class HttpResults
{
    public static IResult ToHttp(this Result result) =>
        result.IsSuccess ? Results.NoContent() : Problem(result.Error!);

    public static IResult ToHttp<T>(this Result<T> result) =>
        result.IsSuccess ? Results.Ok(result.Value) : Problem(result.Error!);

    public static IResult Problem(Error error) =>
        Results.Problem(
            statusCode: error.Kind switch
            {
                ErrorKind.Validation => StatusCodes.Status400BadRequest,
                ErrorKind.NotFound => StatusCodes.Status404NotFound,
                ErrorKind.Conflict => StatusCodes.Status409Conflict,
                ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
                ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status400BadRequest,
            },
            title: error.Code,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code, ["params"] = error.Params });

    /// <summary>Documents the error responses of an endpoint.</summary>
    public static RouteHandlerBuilder ProducesErrors(this RouteHandlerBuilder builder, params int[] statusCodes)
    {
        foreach (var status in statusCodes)
        {
            builder.ProducesProblem(status);
        }
        return builder;
    }
}
