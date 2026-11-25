using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.AgentApi.Endpoints;

/// <summary>
/// Lets agents alert the people of their tenant (Agent Studio → Contacts), e.g. when a plugin run fails.
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agent/notifications")
            .WithTags("AgentAPI - Notifications")
            .RequiresCertificate()
            .AddEndpointFilter<NotificationRateLimitFilter>();

        group.MapPost("/plugin-failure", async (
            [FromBody] PluginFailureNotification request,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IContactService contactService,
            CancellationToken cancellationToken) =>
        {
            if (request == null)
                return Results.BadRequest("Request payload is required");

            // Always the authenticated tenant (certificate, or a matching X-Tenant-Id), never one
            // from the body, so an agent can only email its own tenant's contacts.
            var tenantId = tenantContext.TenantId;
            var result = await contactService.NotifyAllAsync(tenantId,
                PluginFailureEmail.BuildSubject(request),
                PluginFailureEmail.BuildBody(tenantId, request),
                cancellationToken);
            return result.IsSuccess
                ? Results.Ok(new { recipients = result.Data })
                : result.ToHttpResult();
        })
        .WithName("NotifyPluginFailure")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status429TooManyRequests)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithSummary("Email the tenant's contacts about a failed plugin run")
        .WithDescription("Builds a failure alert from the reported run and sends it to every contact of the calling agent's tenant, " +
                         "one email each. Returns how many contacts were emailed (0 when the tenant has no contacts or no email server).");
    }
}
