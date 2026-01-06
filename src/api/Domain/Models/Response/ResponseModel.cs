namespace Domain.Models.Response
{
    public class ResponseModel<T>(bool success, int statusCode, string? message = null, T? data = default)
    {
        public bool HasError { get; private set; } = !success;
        public int StatusCode { get; private set; } = statusCode;
        public string? Message { get; private set; } = message;
        public T? Data { get; private set; } = data;

        public static ResponseModel<T> Ok(T data) => new(true, 200, null, data);
        public static ResponseModel<T> Created(T data) => new(true, 201, null, data);
        public static ResponseModel<T> NoContent() => new(true, 204);
        public static ResponseModel<T> NotFound(string message) => new(false, 404, message);
        public static ResponseModel<T> BadRequest(string message) => new(false, 400, message);
        public static ResponseModel<T> Error(string message, int code = 500) => new(false, code, message);
        public static ResponseModel<T> Unauthorized(string message, int code = 401) => new(false, code, message);

        public object? GetResponse() => HasError ? Message : Data;
    }
}
