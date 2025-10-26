using Microsoft.AspNetCore.Mvc;
using Features.UserApi.Services;
using Shared.Utils.Services;
using System.Text;
using Features.Shared.Configuration;
using Shared.Auth;

namespace Features.UserApi.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        // Map webhook endpoint with API key authentication and rate limiting
        app.MapPost("/api/user/webhooks/{workflow}/{methodName}", async (
            string workflow,
            string methodName,
            [FromQuery] string apikey,
            HttpContext httpContext,
            [FromServices] IWebhookReceiverService webhookService,
            [FromServices] ITenantContext tenantContext) =>
        {
            try
            {
                // Get tenantId from authenticated context (set by EndpointAuthenticationHandler)
                // This prevents IDOR vulnerabilities by ensuring the tenantId matches the authenticated API key
                var tenantId = tenantContext.TenantId;
                
                if (string.IsNullOrEmpty(tenantId))
                {
                    return Results.Problem(
                        detail: "Tenant context not set. Authentication may have failed.",
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                // Read the request body
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();

                // Extract query parameters (excluding apikey which is used for auth)
                var queryParams = httpContext.Request.Query
                    .Where(q => !string.Equals(q.Key, "apikey", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(q => q.Key, q => q.Value.ToString());

                // Process the webhook
                var result = await webhookService.ProcessWebhook(
                    tenantId,
                    workflow,
                    methodName,
                    queryParams,
                    body);

                if (result.IsSuccess)
                {
                    // Return the WebhookResponse directly
                    var webhookResponse = result.Data ?? throw new Exception("WebhookResponse is null");
                    
                    // Apply the webhook response to the HTTP context
                    await webhookResponse.ApplyToHttpContextAsync(httpContext);
                    
                    return Results.Empty;
                }

                return result.ToHttpResult();
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: $"An error occurred while processing the webhook {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("Process Webhook")
        .WithTags("User API - Webhooks")
        .RequireAuthorization("EndpointAuthPolicy")
        .WithAgentUserApiRateLimit() // Apply rate limiting to prevent enumeration attacks
        .WithOpenApi(operation =>
        {
            operation.Summary = "Process a webhook for a temporal workflow";
            operation.Description = @"Receives webhook calls and delivers them as Temporal Updates to the specified workflow.
            
The webhook URL format is:
POST /api/user/webhooks/{workflow}/{methodName}?apikey={apikey}

Where:
- workflow: Either the WorkflowId or WorkflowType
- methodName: The name of the Temporal Update method to call
- apikey: A valid API key for authentication (query parameter)

The tenant is automatically determined from the authenticated API key.

The workflow's Update method should have the signature:
[Update(""method-name"")]
public async Task<string> WebhookUpdateMethod(IDictionary<string, string> queryParams, string body)

Query parameters (except apikey) are passed to the Update method.
The request body is passed as a string to the Update method.
The Update method should return a string response.";
            return operation;
        })
        .Produces<string>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
        .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);
    }
}
