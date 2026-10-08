namespace TimingApp.Domain.SharedKernel;

/// <summary>Category of an expected failure; decides the HTTP status at the API boundary.</summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Unauthorized,
    Unavailable,
}

/// <summary>
/// Expected failure with a stable code (part of the API contract, e.g. <c>registration.startNumberTaken</c>)
/// and optional parameters for the translated client message.
/// </summary>
public sealed record Error(string Code, ErrorKind Kind, IReadOnlyDictionary<string, object?> Params)
{
    public static Error Validation(string code, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new(code, ErrorKind.Validation, parameters ?? Empty);

    public static Error NotFound(string code) => new(code, ErrorKind.NotFound, Empty);

    public static Error Conflict(string code, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new(code, ErrorKind.Conflict, parameters ?? Empty);

    public static Error Unauthorized(string code) => new(code, ErrorKind.Unauthorized, Empty);

    public static Error Unavailable(string code, IReadOnlyDictionary<string, object?>? parameters = null) =>
        new(code, ErrorKind.Unavailable, parameters ?? Empty);

    private static readonly IReadOnlyDictionary<string, object?> Empty = new Dictionary<string, object?>();
}
