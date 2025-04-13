namespace Features.AgentApi.Services.Agent;

public enum StatusCode
{
    Ok = 200,
    BadRequest = 400,
    Unauthorized = 401,
    Forbidden = 403,
    NotFound = 404,
    Conflict = 409,
}

/// <summary>
/// Represents a result of a service operation, independent of the web layer
/// </summary>
/// <typeparam name="T">The type of data returned on success</typeparam>
public class ServiceResult<T>
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public T? Data { get; }
    public StatusCode StatusCode { get; }

    private ServiceResult(bool isSuccess, T? data, string? errorMessage, StatusCode statusCode)
    {
        IsSuccess = isSuccess;
        Data = data;
        ErrorMessage = errorMessage;
        StatusCode = statusCode;
    }

    public static ServiceResult<T> Success(T data, StatusCode statusCode = StatusCode.Ok)
    {
        return new ServiceResult<T>(true, data, null, statusCode);
    }

    public static ServiceResult<T> BadRequest(string errorMessage, StatusCode statusCode = StatusCode.BadRequest)
    {
        return new ServiceResult<T>(false, default, errorMessage, statusCode);
    }

    public static ServiceResult<T> Forbidden(string errorMessage, StatusCode statusCode = StatusCode.Forbidden)
    {
        return new ServiceResult<T>(false, default, errorMessage, statusCode);
    }

    public static ServiceResult<T> NotFound(string errorMessage, StatusCode statusCode = StatusCode.NotFound)
    {
        return new ServiceResult<T>(false, default, errorMessage, statusCode);
    }

    public static ServiceResult<T> Conflict(string errorMessage, StatusCode statusCode = StatusCode.Conflict)
    {
        return new ServiceResult<T>(false, default, errorMessage, statusCode);
    }

    public static ServiceResult<T> Unauthorized(string errorMessage, StatusCode statusCode = StatusCode.Unauthorized)
    {
        return new ServiceResult<T>(false, default, errorMessage, statusCode);
    }
}

// Non-generic version for operations without return data
public class ServiceResult
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }
    public int StatusCode { get; }

    private ServiceResult(bool isSuccess, string? errorMessage, int statusCode)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
        StatusCode = statusCode;
    }

    public static ServiceResult Success(int statusCode = 200)
    {
        return new ServiceResult(true, null, statusCode);
    }

    public static ServiceResult Failure(string errorMessage, int statusCode = 400)
    {
        return new ServiceResult(false, errorMessage, statusCode);
    }
} 