using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Features.AdminApi.Utils;

/// <summary>
/// Middleware that logs detailed request and response information for AdminApi endpoints.
/// This is useful for debugging and monitoring AdminApi calls.
/// Only active when AdminApi:EnableDebugLogging is set to true in configuration.
/// </summary>
public class AdminApiDebugLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AdminApiDebugLoggingMiddleware> _logger;
    private readonly bool _isEnabled;

    public AdminApiDebugLoggingMiddleware(
        RequestDelegate next, 
        ILogger<AdminApiDebugLoggingMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _isEnabled = configuration.GetValue<bool>("AdminApi:EnableDebugLogging", false);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only log if enabled and path starts with /api/v{version}/admin
        if (!_isEnabled || !IsAdminApiPath(context.Request.Path))
        {
            await _next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N");

        // Log request
        await LogRequest(context, requestId);

        // Capture response
        var originalBodyStream = context.Response.Body;
        using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        try
        {
            await _next(context);
            stopwatch.Stop();

            // Log response
            await LogResponse(context, requestId, stopwatch.ElapsedMilliseconds);

            // Copy the response back to the original stream
            responseBody.Seek(0, SeekOrigin.Begin);
            await responseBody.CopyToAsync(originalBodyStream);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, 
                "[AdminAPI Debug] [RequestId: {RequestId}] Exception occurred after {ElapsedMs}ms", 
                requestId, 
                stopwatch.ElapsedMilliseconds);
            throw;
        }
        finally
        {
            context.Response.Body = originalBodyStream;
        }
    }

    private async Task LogRequest(HttpContext context, string requestId)
    {
        var request = context.Request;
        
        // Build request log
        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"[AdminAPI Debug] ===== REQUEST START [RequestId: {requestId}] =====");
        logBuilder.AppendLine($"Method: {request.Method}");
        logBuilder.AppendLine($"Path: {request.Path}");
        logBuilder.AppendLine($"QueryString: {request.QueryString}");
        logBuilder.AppendLine($"Scheme: {request.Scheme}");
        logBuilder.AppendLine($"Host: {request.Host}");
        
        // Log headers (excluding sensitive ones)
        logBuilder.AppendLine("Headers:");
        foreach (var (key, value) in request.Headers)
        {
            if (IsSensitiveHeader(key))
            {
                logBuilder.AppendLine($"  {key}: [REDACTED]");
            }
            else
            {
                logBuilder.AppendLine($"  {key}: {value}");
            }
        }

        // Log request body for non-GET requests
        if (request.Method != "GET" && request.Method != "DELETE")
        {
            request.EnableBuffering();
            var buffer = new byte[Convert.ToInt32(request.ContentLength ?? 0)];
            
            if (buffer.Length > 0)
            {
                await request.Body.ReadExactlyAsync(buffer.AsMemory(0, buffer.Length));
                var bodyAsText = Encoding.UTF8.GetString(buffer);
                request.Body.Position = 0; // Reset position for next middleware
                
                logBuilder.AppendLine("Request Body:");
                logBuilder.AppendLine(FormatJson(bodyAsText));
            }
            else
            {
                logBuilder.AppendLine("Request Body: [Empty]");
            }
        }
        
        logBuilder.AppendLine($"[AdminAPI Debug] ===== REQUEST END [RequestId: {requestId}] =====");
        
        _logger.LogDebug(logBuilder.ToString());
    }

    private async Task LogResponse(HttpContext context, string requestId, long elapsedMs)
    {
        var response = context.Response;
        
        // Build response log
        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"[AdminAPI Debug] ===== RESPONSE START [RequestId: {requestId}] =====");
        logBuilder.AppendLine($"Status Code: {response.StatusCode}");
        logBuilder.AppendLine($"Content-Type: {response.ContentType}");
        logBuilder.AppendLine($"Elapsed Time: {elapsedMs}ms");
        
        // Log headers (excluding sensitive ones)
        logBuilder.AppendLine("Headers:");
        foreach (var (key, value) in response.Headers)
        {
            if (IsSensitiveHeader(key))
            {
                logBuilder.AppendLine($"  {key}: [REDACTED]");
            }
            else
            {
                logBuilder.AppendLine($"  {key}: {value}");
            }
        }

        // Log response body
        response.Body.Seek(0, SeekOrigin.Begin);
        var bodyAsText = await new StreamReader(response.Body).ReadToEndAsync();
        response.Body.Seek(0, SeekOrigin.Begin);
        
        if (!string.IsNullOrEmpty(bodyAsText))
        {
            logBuilder.AppendLine("Response Body:");
            logBuilder.AppendLine(FormatJson(bodyAsText));
        }
        else
        {
            logBuilder.AppendLine("Response Body: [Empty]");
        }
        
        logBuilder.AppendLine($"[AdminAPI Debug] ===== RESPONSE END [RequestId: {requestId}] =====");
        
        _logger.LogDebug(logBuilder.ToString());
    }

    private static bool IsAdminApiPath(PathString path)
    {
        // Match paths like /api/v1/admin/, /api/v2/admin/, etc.
        return path.StartsWithSegments("/api") && 
               path.Value?.Contains("/admin/", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsSensitiveHeader(string headerName)
    {
        var sensitiveHeaders = new[]
        {
            "authorization",
            "x-api-key",
            "cookie",
            "set-cookie",
            "x-admin-api-key"
        };
        
        return sensitiveHeaders.Contains(headerName.ToLowerInvariant());
    }

    private static string FormatJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
            return JsonSerializer.Serialize(jsonElement, new JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
        }
        catch
        {
            // If it's not valid JSON, return as-is
            return json;
        }
    }
}
