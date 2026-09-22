namespace Uniqua.Projector.Domain;

/// <summary>
/// The outcome of an operation that is allowed to refuse: either a value or the reason it was
/// refused. It exists so an invariant can hand back <em>which</em> rule was broken — a bare
/// boolean would force the caller to guess, and guessing is how one refusal ends up worded as
/// another.
/// </summary>
public readonly struct Result<TValue, TError>
{
    private readonly TValue? _value;

    private Result(bool isSuccess, TValue? value, TError? error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    /// <summary>The reason for the refusal, or <c>null</c> on success.</summary>
    public TError? Error { get; }

    /// <summary>The value. Reading it on a refusal is a programming error, not a null.</summary>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            $"This result is a refusal ({Error}); it has no value to read.");

    public static Result<TValue, TError> Success(TValue value) => new(true, value, default);

    public static Result<TValue, TError> Failure(TError error) => new(false, default, error);
}
