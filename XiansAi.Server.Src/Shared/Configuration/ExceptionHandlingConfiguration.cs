using Microsoft.AspNetCore.Diagnostics;
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
