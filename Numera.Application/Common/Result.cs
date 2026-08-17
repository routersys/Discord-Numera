namespace Numera.Application.Common;

public readonly struct Result
{
    private Result(ApplicationError? error) => Error = error;

    public ApplicationError? Error { get; }

    public bool IsSuccess => Error is null;

    public static Result Success() => new(null);

    public static Result Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result Failure(ErrorCategory category, string code, string? field = null) =>
        Failure(ApplicationError.Create(category, code, field));
}

public readonly struct Result<TValue>
{
    private readonly TValue? value;

    private Result(TValue? value, ApplicationError? error)
    {
        this.value = value;
        Error = error;
    }

    public ApplicationError? Error { get; }

    public bool IsSuccess => Error is null;

    public TValue Value => IsSuccess
        ? value!
        : throw new InvalidOperationException("失敗した Result から値を取得することはできません。");

    public static Result<TValue> Success(TValue value) => new(value, null);

    public static Result<TValue> Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(default, error);
    }

    public static Result<TValue> Failure(ErrorCategory category, string code, string? field = null) =>
        Failure(ApplicationError.Create(category, code, field));

    public Result<TNext> Map<TNext>(Func<TValue, TNext> projection)
    {
        ArgumentNullException.ThrowIfNull(projection);

        return IsSuccess
            ? Result<TNext>.Success(projection(value!))
            : Result<TNext>.Failure(Error!);
    }

    public Result Discard() => IsSuccess ? Result.Success() : Result.Failure(Error!);
}
