

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
            StatusCode.BadRequest => Results.BadRequest(result.ErrorMessage),
            StatusCode.Unauthorized => Results.Unauthorized(),
            StatusCode.Forbidden => Results.Forbid(),
            StatusCode.NotFound => Results.NotFound(result.ErrorMessage),
            StatusCode.Conflict => Results.Conflict(result.ErrorMessage),
            _ => Results.StatusCode((int)result.StatusCode)
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
            400 => Results.BadRequest(result.ErrorMessage),
            401 => Results.Unauthorized(),
            403 => Results.Forbid(),
            404 => Results.NotFound(result.ErrorMessage),
            409 => Results.Conflict(result.ErrorMessage),
            _ => Results.StatusCode(result.StatusCode)
        };
    }
} 