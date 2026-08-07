namespace FFXIV.RiichiAssistant.Plugin.Game;

public readonly record struct Result<TValue, TError>(TValue? Value, TError? Error)
    where TValue : class
    where TError : class
{
    public bool IsSuccess => Value is not null;

    public static Result<TValue, TError> Success(TValue value) => new(value, null);

    public static Result<TValue, TError> Failure(TError error) => new(null, error);
}
