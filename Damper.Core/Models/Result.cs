namespace Damper.Core.Models
{
    public class Result<TValue>
    {
        public bool IsSuccess { get; }
        public bool IsFailure => !IsSuccess;
        public TValue? Value { get; }
        public Error Error { get; }

        private Result(bool isSuccess, TValue? value, Error error)
        {
            IsSuccess = isSuccess;
            Value = value;
            Error = error;
        }

        // Factory methods for clean code semantics
        public static Result<TValue> Success(TValue value) => new(true, value, new Error(ErrorType.None, string.Empty));

        public static Result<TValue> Failure(ErrorType type, string message) => new(false, default, new Error(type, message));
    }

    public record Error(ErrorType Type, string Message);

    public enum ErrorType
    {
        None = 0,
        BadRequest = 1,
        NotFound = 2,
        Conflict = 3,
        ServerError = 4
    }
}