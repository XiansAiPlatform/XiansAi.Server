using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using System.Text;
using System.Text.Json;

namespace Features.Shared.Configuration;

public static class ExceptionHandlingConfiguration
{
    public static WebApplication UseExceptionHandlingConfiguration(this WebApplication app)
    {
        app.UseExceptionHandler(errorApp =>
        {
            errorApp.Run(async context =>
            {
                var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                
                context.Response.ContentType = "application/json";
                
                switch (exception)
                {
                    case BadHttpRequestException badRequestEx when badRequestEx.InnerException is JsonException jsonEx:
                        // Handle model binding validation errors
                        logger.LogInformation("Model binding error: {Message}", jsonEx.Message);
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new 
                        { 
                            error = "Invalid request format",
                            message = jsonEx.Message
                        });
                        break;
                        
                    case BadHttpRequestException badRequestEx:
                        // Handle other bad request exceptions
                        logger.LogInformation("Bad request error: {Message}", badRequestEx.Message);
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new 
                        { 
                            error = "Bad request",
                            message = badRequestEx.Message
                        });
                        break;
                        
                    default:
                        // Handle other exceptions
                        logger.LogError(exception, "An unhandled exception occurred");
                        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                        await context.Response.WriteAsJsonAsync(new 
                        { 
                            error = "An error occurred while processing your request",
                            message = exception?.Message ?? "Unknown error"
                        });
                        break;

                }
            });
        });
        
        return app;
    }
}

/// <summary>
/// Custom authorization middleware result handler that provides detailed error messages
/// </summary>
public class AuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly ILogger<AuthorizationMiddlewareResultHandler> _logger;

    public AuthorizationMiddlewareResultHandler(ILogger<AuthorizationMiddlewareResultHandler> logger)
    {
        _logger = logger;
    }

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        // If authorization succeeded, continue with the request
        if (authorizeResult.Succeeded)
        {
            await next(context);
            return;
        }

        // If the user is not authenticated, return 401
        if (authorizeResult.Challenged)
        {
            // Check if response has already started
            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Cannot write 401 response - response has already started for path: {Path}", context.Request.Path);
                return;
            }

            var responseBody = new
            {
                error = "Unauthorized",
                message = "Authentication is required to access this resource"
            };
            
            // Serialize to string first, then write as bytes
            var jsonString = JsonSerializer.Serialize(responseBody, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });
            var bytes = Encoding.UTF8.GetBytes(jsonString);
            
            // Set response properties BEFORE writing
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = bytes.Length;
            
            await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
            await context.Response.Body.FlushAsync();
            await context.Response.CompleteAsync();
            
            _logger.LogWarning("Authentication challenge returned for path: {Path}, wrote response body ({Length} bytes): {JsonString}", 
                context.Request.Path, bytes.Length, jsonString);
            return;
        }

        // If authorization failed (403), provide detailed error message
        if (authorizeResult.Forbidden)
        {
            // Check if response has already started
            if (context.Response.HasStarted)
            {
                _logger.LogWarning("Cannot write 403 response - response has already started for path: {Path}", context.Request.Path);
                return;
            }

            // Extract failure reasons
            var failureReasons = authorizeResult.AuthorizationFailure?.FailureReasons
                .Select(r => r.Message)
                .Where(m => !string.IsNullOrEmpty(m))
                .ToList() ?? new List<string>();

            var errorMessage = failureReasons.Any()
                ? string.Join("; ", failureReasons)
                : "You do not have permission to access this resource";

            _logger.LogInformation("Authorization forbidden for path: {Path}, Reasons: {Reasons}", 
                context.Request.Path, 
                string.Join("; ", failureReasons));

            var responseBody = new
            {
                error = "Forbidden",
                message = errorMessage
            };

            // Serialize to string first, then write as bytes
            var jsonString = JsonSerializer.Serialize(responseBody, new JsonSerializerOptions 
            { 
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase 
            });
            var bytes = Encoding.UTF8.GetBytes(jsonString);
            
            // Set response properties BEFORE writing
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = bytes.Length;
            
            await context.Response.Body.WriteAsync(bytes, 0, bytes.Length);
            await context.Response.Body.FlushAsync();
            await context.Response.CompleteAsync();
            
            _logger.LogInformation("Wrote 403 response body ({Length} bytes): {JsonString}", bytes.Length, jsonString);
            
            return;
        }

        // If we get here, something unexpected happened - just continue with the request
        await next(context);
    }
} 
