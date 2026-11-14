namespace Consent.Shared;

/// <summary>Kết quả của một thao tác nghiệp vụ: thành công hoặc kèm <see cref="Shared.Error"/>.</summary>
public class Result
{
    protected Result(Error? error) => Error = error;

    public Error? Error { get; }

    public bool IsSuccess => Error is null;

    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result(error);
    }

    public static Result<T> Success<T>(T value) => Result<T>.Success(value);
}

/// <summary>Kết quả mang giá trị. Truy cập <see cref="Value"/> khi thất bại sẽ ném ngoại lệ.</summary>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(T? value, Error? error)
        : base(error) => _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Không thể đọc giá trị của kết quả thất bại.");

    public static Result<T> Success(T value) => new(value, null);

    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<T>(default, error);
    }

    /// <summary>Cho phép <c>return dto;</c> trong phương thức trả về <see cref="Result{T}"/>.</summary>
    public static implicit operator Result<T>(T value) => Success(value);

    /// <summary>Cho phép <c>return error;</c> trong phương thức trả về <see cref="Result{T}"/>.</summary>
    public static implicit operator Result<T>(Error error) => Failure(error);

    /// <summary>Tên thay thế cho toán tử chuyển đổi (CA2225).</summary>
    public static Result<T> FromValue(T value) => Success(value);

    /// <summary>Tên thay thế cho toán tử chuyển đổi (CA2225).</summary>
    public static Result<T> FromError(Error error) => Failure(error);
}
