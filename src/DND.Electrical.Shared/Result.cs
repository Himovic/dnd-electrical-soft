namespace DND.Electrical.Shared;

/// <summary>Outcome of an operation that can fail for expected (non-exceptional) reasons.</summary>
public class Result
{
    protected Result(bool success, string? error)
    {
        IsSuccess = success;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string? Error { get; }

    public static Result Ok() => new(true, null);
    public static Result Fail(string error) => new(false, error);
    public static Result<T> Ok<T>(T value) => new(value, true, null);
    public static Result<T> Fail<T>(string error) => new(default, false, error);
}

public sealed class Result<T> : Result
{
    internal Result(T? value, bool success, string? error) : base(success, error) => Value = value;

    public T? Value { get; }

    public T GetValueOrThrow() =>
        IsSuccess && Value is not null ? Value : throw new InvalidOperationException(Error ?? "No value.");
}
