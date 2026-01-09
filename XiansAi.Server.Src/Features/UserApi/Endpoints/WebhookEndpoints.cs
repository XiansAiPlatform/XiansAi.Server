using Microsoft.AspNetCore.Mvc;
using Features.UserApi.Services;
using Shared.Utils.Services;
using System.Text;
using System.Text.Json;
using Features.Shared.Configuration;
using Shared.Auth;
using Shared.Services;
using Shared.Repositories;
using Shared.Utils;
using Shared.Models;

namespace Features.UserApi.Endpoints;

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        // Map builtin webhook endpoint for synchronous message passing
        app.MapPost("/api/user/webhooks/builtin", async (
            [FromQuery] string workflowName,
            [FromQuery] string webhookName,
            [FromQuery] string agentName,
            [FromQuery] string apikey,
            HttpContext httpContext,
            [FromServices] IMessageService messageService,
            [FromServices] IPendingRequestService pendingRequestService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IFlowDefinitionRepository flowDefinitionRepository,
            [FromServices] ILogger<SyncMessageHandler> logger,
            [FromQuery] int timeoutSeconds = 60,
            [FromQuery] string? participantId = null,
            [FromQuery] string? scope = null,
            [FromQuery] string? authorization = null) =>
        {
            try
            {
                // Validate required parameters
                if (string.IsNullOrWhiteSpace(workflowName))
                {
                    return Results.BadRequest("workflowName is required");
                }

                if (string.IsNullOrWhiteSpace(webhookName))
                {
                    return Results.BadRequest("webhookName is required");
                }

                if (string.IsNullOrWhiteSpace(agentName))
                {
                    return Results.BadRequest("agentName is required");
                }

                // Get tenantId from authenticated context (derived from API key)
                var tenantId = tenantContext.TenantId;
                
                if (string.IsNullOrEmpty(tenantId))
                {
                    return Results.Problem(
                        detail: "Tenant context not set. Authentication may have failed.",
                        statusCode: StatusCodes.Status401Unauthorized);
                }

                // Validate timeout is within acceptable range
                if (timeoutSeconds <= 0 || timeoutSeconds > 300)
                {
                    return Results.BadRequest("Timeout must be between 1 and 300 seconds");
                }

                // Validate that the flow definition exists for this agent, workflow, and tenant
                // Build the workflow type in the format: "AgentName:WorkflowName"
                var workflowType = $"{agentName}:{workflowName}";
                var flowDefinition = await flowDefinitionRepository.GetLatestFlowDefinitionAsync(workflowType, tenantId);
                
                if (flowDefinition == null)
                {
                    logger.LogWarning(
                        "Flow definition not found for webhook request. WorkflowType: {WorkflowType}, Tenant: {TenantId}, Agent: {AgentName}, Workflow: {WorkflowName}", 
                        workflowType, tenantId, agentName, workflowName);
                    
                    return Results.Problem(
                        detail: $"Flow definition not found for agent '{agentName}' and workflow '{workflowName}' in tenant '{tenantId}'. Please ensure the workflow is deployed.",
                        statusCode: StatusCodes.Status404NotFound);
                }

                logger.LogDebug(
                    "Flow definition validated successfully. WorkflowType: {WorkflowType}, Tenant: {TenantId}", 
                    workflowType, tenantId);

                // Read the request body
                using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();

                // Extract query parameters (excluding system parameters)
                var excludedParams = new[] { "apikey", "timeoutSeconds", "participantId", "agentName", "scope", "authorization" };
                var queryParams = httpContext.Request.Query
                    .Where(q => !excludedParams.Contains(q.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(q => q.Key, q => q.Value.ToString());

                // Build workflowId from workflowName (format: tenant:workflowName)
                // Using webhookName as the scope/method identifier
                var workflowId = $"{tenantId}:{workflowType}";
                
                // Use participantId from query or default to "webhook" for identification
                var resolvedParticipantId = participantId ?? "webhook";

                // Generate unique request ID for correlation
                var requestId = MessageRequestProcessor.GenerateRequestId(workflowId, resolvedParticipantId);

                // Create the chat request for the message service
                var chatRequest = new ChatOrDataRequest
                {
                    RequestId = requestId,
                    ParticipantId = resolvedParticipantId,
                    WorkflowId = workflowId,
                    Text = webhookName,
                    Data = body,
                    Scope = scope,
                    Authorization = authorization ?? tenantContext.Authorization,
                    Origin = $"webhook:builtin:{webhookName}"
                };

                // Use the sync message handler to process and wait for response
                var syncHandler = new SyncMessageHandler(messageService, pendingRequestService, logger);
                var result = await syncHandler.ProcessSyncMessageAsync(
                    chatRequest,
                    MessageType.Webhook,
                    timeoutSeconds,
                    httpContext.RequestAborted);

                // Handle error results
                if (result is IResult httpResult)
                {
                    return httpResult;
                }

                // Parse WebhookResponse from the result
                // The result is an anonymous object with properties: ThreadId, Text, Data, etc.
                WebhookResponse? webhookResponse = null;

                // JSON serializer options with case-insensitive property matching
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                // Try to extract WebhookResponse from Data field
                if (result is not null)
                {
                    var resultType = result.GetType();
                    var dataProperty = resultType.GetProperty("Data");
                    var textProperty = resultType.GetProperty("Text");

                    if (dataProperty != null)
                    {
                        var dataValue = dataProperty.GetValue(result);
                        
                        // If Data is already a WebhookResponse
                        if (dataValue is WebhookResponse wr)
                        {
                            webhookResponse = wr;
                        }
                        // If Data is a JsonElement, try to deserialize it
                        else if (dataValue is JsonElement jsonElement)
                        {
                            try
                            {
                                webhookResponse = JsonSerializer.Deserialize<WebhookResponse>(jsonElement.GetRawText(), jsonOptions);
                                logger.LogDebug("Successfully deserialized WebhookResponse from JsonElement");
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to deserialize WebhookResponse from JsonElement. Data: {Data}", jsonElement.GetRawText());
                            }
                        }
                        // If Data is a dictionary or any other object, serialize and deserialize
                        else if (dataValue != null)
                        {
                            try
                            {
                                var json = JsonSerializer.Serialize(dataValue);
                                webhookResponse = JsonSerializer.Deserialize<WebhookResponse>(json, jsonOptions);
                                logger.LogDebug("Successfully deserialized WebhookResponse from object. StatusCode: {StatusCode}", webhookResponse?.StatusCode);
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to deserialize WebhookResponse from object. Type: {Type}", dataValue.GetType().Name);
                            }
                        }
                    }

                    // Fallback: try to parse from Text field if Data didn't work
                    if (webhookResponse == null && textProperty != null)
                    {
                        var textValue = textProperty.GetValue(result) as string;
                        if (!string.IsNullOrEmpty(textValue))
                        {
                            try
                            {
                                webhookResponse = JsonSerializer.Deserialize<WebhookResponse>(textValue, jsonOptions);
                                logger.LogDebug("Successfully deserialized WebhookResponse from Text field");
                            }
                            catch (Exception ex)
                            {
                                logger.LogWarning(ex, "Failed to deserialize WebhookResponse from Text field. Text: {Text}", textValue);
                            }
                        }
                    }
                }

                // Apply the WebhookResponse to the HTTP context
                if (webhookResponse != null)
                {
                    logger.LogInformation(
                        "Applying WebhookResponse: StatusCode={StatusCode}, ContentType={ContentType}, HeaderCount={HeaderCount}", 
                        webhookResponse.StatusCode, 
                        webhookResponse.ContentType, 
                        webhookResponse.Headers?.Count ?? 0);
                    
                    await webhookResponse.ApplyToHttpContextAsync(httpContext);
                    return Results.Empty;
                }

                // Fallback: return result as JSON if no WebhookResponse could be parsed
                logger.LogWarning("Could not parse WebhookResponse from result, returning raw result");
                return Results.Ok(result);
            }
            catch (TimeoutException)
            {
                return Results.Problem(
                    detail: "Request timed out waiting for workflow response",
                    statusCode: StatusCodes.Status408RequestTimeout);
            }
            catch (OperationCanceledException)
            {
                return Results.Problem(
                    detail: "Request was cancelled",
                    statusCode: 499); // Client Closed Request
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: $"An error occurred while processing the builtin webhook: {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("Process Builtin Webhook")
        .WithTags("User API - Webhooks")
        .RequireAuthorization("EndpointAuthPolicy")
        .WithAgentUserApiRateLimit()
        .WithOpenApi(operation =>
        {
            operation.Summary = "Process a builtin webhook using message passing infrastructure";
            operation.Description = @"Receives webhook calls and delivers them as messages to the specified workflow,
waiting synchronously for a response using the message passing infrastructure.
            
The webhook URL format is:
POST /api/user/webhooks/builtin?workflowName={workflowName}&webhookName={webhookName}&agentName={agentName}&apikey={apikey}

Query Parameters (Required):
- workflowName: The name of the builtin workflow to send the message to (e.g., 'IntegrationAgent' or 'My Integration Agent')
- webhookName: The name of the webhook, used as scope for the message (can contain spaces and special characters)
- agentName: The agent name (e.g., 'Integration Agent' or 'My Custom Agent')
- apikey: A valid API key for authentication

Query Parameters (Optional):
- timeoutSeconds: Timeout for waiting for response in seconds (default: 60, max: 300)
- participantId: ID for the system or user invoking the webhook (default: 'webhook')
- scope: An optional scope or category identifier for the webhook
- authorization: An optional authorization token to override the default tenant authorization

The tenant is automatically determined from the authenticated API key.
Using query parameters allows workflowName, webhookName, and agentName to contain spaces and special characters without URL encoding issues.

The workflow will receive a message with MessageType.Webhook containing:
- webhookName: The name of the webhook
- scope: Optional scope or category identifier (null if not provided)
- queryParams: Dictionary of query parameters (excluding system params)
- body: The raw request body as a string
- contentType: The content-type header of the request

The workflow should respond with an outgoing message of MessageType.Webhook containing a WebhookResponse object in the Data field.
The WebhookResponse should have: StatusCode, Headers, Content, and ContentType properties.
This allows the workflow to control the HTTP response returned to the webhook caller.";
            return operation;
        })
        .Produces<object>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
        .Produces<ProblemDetails>(StatusCodes.Status408RequestTimeout)
        .Produces<ProblemDetails>(StatusCodes.Status500InternalServerError);

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
