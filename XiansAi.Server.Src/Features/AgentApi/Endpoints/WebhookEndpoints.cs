using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Features.AgentApi.Endpoints;

/// <summary>
/// Logger category for agent webhook endpoints.
/// </summary>
public class AgentWebhookEndpointsLogger { }

/// <summary>
/// AgentApi endpoints that allow a certificate-authenticated agent to manage its own
/// builtin webhooks (create, list, delete). The tenant is resolved from the certificate
/// context rather than a route parameter. These reuse the shared <see cref="IAppIntegrationService"/>
/// builtin-webhook operations, mirroring the AdminApi webhook endpoints.
/// </summary>
public static class WebhookEndpoints
{
    /// <summary>
    /// Maps all agent webhook endpoints under /api/agent/webhooks.
    /// </summary>
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        var webhookGroup = app.MapGroup("/api/agent/webhooks")
            .WithTags("AgentAPI - Webhooks")
            .RequiresCertificate();

        webhookGroup.MapPost("", CreateWebhook)
            .WithName("Agent Create Webhook")
            .Produces<AppIntegrationResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithSummary("Create a builtin webhook")
            .WithDescription("Creates a builtin webhook (API key + app integration) for the calling agent's tenant.");

        webhookGroup.MapGet("", ListWebhooks)
            .WithName("Agent List Webhooks")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithSummary("List builtin webhooks")
            .WithDescription("Lists builtin webhooks for the calling agent's tenant, optionally filtered by agentName and activationName.");

        webhookGroup.MapDelete("/{integrationId}", DeleteWebhook)
            .WithName("Agent Delete Webhook")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithSummary("Delete a builtin webhook")
            .WithDescription("Deletes a builtin webhook (revokes API key + deletes integration) for the calling agent's tenant.");
    }

    private static async Task<IResult> CreateWebhook(
        [FromBody] CreateBuiltinWebhookRequest request,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] ITenantContext tenantContext,
        [FromServices] ILogger<AgentWebhookEndpointsLogger> logger)
    {
        var tenantId = tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            logger.LogWarning("TenantId could not be resolved from certificate context");
            return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
        }

        var createdBy = string.IsNullOrWhiteSpace(tenantContext.LoggedInUser) ? "system" : tenantContext.LoggedInUser;

        logger.LogInformation(
            "Agent creating builtin webhook for tenant {TenantId}, agent {AgentName}, activation {ActivationName}",
            LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(request.AgentName), LogSanitizer.Sanitize(request.ActivationName));

        var result = await integrationService.CreateBuiltinWebhookAsync(request, tenantId, createdBy);
        return result.ToHttpResult();
    }

    private static async Task<IResult> ListWebhooks(
        [FromQuery] string? activationName,
        [FromQuery] string? agentName,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] ITenantContext tenantContext,
        [FromServices] ILogger<AgentWebhookEndpointsLogger> logger)
    {
        var tenantId = tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            logger.LogWarning("TenantId could not be resolved from certificate context");
            return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await integrationService.GetBuiltinWebhooksAsync(tenantId, activationName, agentName);
        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        return Results.Ok(new { webhooks = result.Data });
    }

    private static async Task<IResult> DeleteWebhook(
        string integrationId,
        [FromServices] IAppIntegrationService integrationService,
        [FromServices] ITenantContext tenantContext,
        [FromServices] ILogger<AgentWebhookEndpointsLogger> logger)
    {
        var tenantId = tenantContext.TenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            logger.LogWarning("TenantId could not be resolved from certificate context");
            return Results.Problem("TenantId could not be resolved", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await integrationService.DeleteBuiltinWebhookAsync(integrationId, tenantId);
        if (!result.IsSuccess)
        {
            return result.ToHttpResult();
        }

        return Results.Ok(new { message = "Webhook deleted successfully" });
    }
}
