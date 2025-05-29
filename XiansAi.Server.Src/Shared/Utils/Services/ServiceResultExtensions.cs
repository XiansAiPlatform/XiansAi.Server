using Microsoft.AspNetCore.Http;

namespace Shared.Utils.Services;

/// <summary>
/// Extension methods for converting ServiceResult to HTTP results
/// </summary>
public static class ServiceResultExtensions
{
    /// <summary>
    /// Converts a ServiceResult to an appropriate IResult for HTTP responses
    /// </summary>
    public static IResult ToHttpResult<T>(this ServiceResult<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Data);
        }

        return result.StatusCode switch
        {
            StatusCode.BadRequest => Results.Json(
                new { error = result.ErrorMessage ?? "Bad request" }, 
                statusCode: StatusCodes.Status400BadRequest),
            StatusCode.Unauthorized => Results.Json(
                new { error = result.ErrorMessage ?? "Unauthorized" }, 
                statusCode: StatusCodes.Status401Unauthorized),
            StatusCode.Forbidden => Results.Json(
                new { error = result.ErrorMessage ?? "Forbidden" }, 
                statusCode: StatusCodes.Status403Forbidden),
            StatusCode.NotFound => Results.Json(
                new { error = result.ErrorMessage ?? "Not found" }, 
                statusCode: StatusCodes.Status404NotFound),
            StatusCode.Conflict => Results.Json(
                new { error = result.ErrorMessage ?? "Conflict" }, 
                statusCode: StatusCodes.Status409Conflict),
            StatusCode.InternalServerError => Results.Json(
                new { error = result.ErrorMessage ?? "Internal server error" }, 
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Json(
                new { error = result.ErrorMessage ?? "An error occurred" }, 
                statusCode: (int)result.StatusCode)
        };
    }

    /// <summary>
    /// Converts a non-generic ServiceResult to an appropriate IResult for HTTP responses
    /// </summary>
    public static IResult ToHttpResult(this ServiceResult result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok();
        }

        return result.StatusCode switch
        {
            StatusCode.BadRequest => Results.Json(
                new { error = result.ErrorMessage ?? "Bad request" }, 
                statusCode: StatusCodes.Status400BadRequest),
            StatusCode.Unauthorized => Results.Json(
                new { error = result.ErrorMessage ?? "Unauthorized" }, 
                statusCode: StatusCodes.Status401Unauthorized),
            StatusCode.Forbidden => Results.Json(
                new { error = result.ErrorMessage ?? "Forbidden" }, 
                statusCode: StatusCodes.Status403Forbidden),
            StatusCode.NotFound => Results.Json(
                new { error = result.ErrorMessage ?? "Not found" }, 
                statusCode: StatusCodes.Status404NotFound),
            StatusCode.Conflict => Results.Json(
                new { error = result.ErrorMessage ?? "Conflict" }, 
                statusCode: StatusCodes.Status409Conflict),
            StatusCode.InternalServerError => Results.Json(
                new { error = result.ErrorMessage ?? "Internal server error" }, 
                statusCode: StatusCodes.Status500InternalServerError),
            _ => Results.Json(
                new { error = result.ErrorMessage ?? "An error occurred" }, 
                statusCode: (int)result.StatusCode)
        };
    }
} 