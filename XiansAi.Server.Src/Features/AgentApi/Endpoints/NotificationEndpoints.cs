using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.AgentApi.Endpoints;

/// <summary>An email for every contact of the calling agent's tenant.</summary>
public sealed record ContactNotificationRequest(string? Subject, string? Body);

/// <summary>
/// Lets agents alert the people of their tenant (Agent Studio → Contacts), e.g. when a plugin run fails.
/// </summary>
public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/agent/notifications")
            .WithTags("AgentAPI - Notifications")
            .RequiresCertificate();

        group.MapPost("/contacts", async (
            [FromBody] ContactNotificationRequest request,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IContactService contactService) =>
        {
            // Always the authenticated tenant (certificate, or a matching X-Tenant-Id), never one
            // from the body, so an agent can only email its own tenant's contacts.
            var result = await contactService.NotifyAllAsync(tenantContext.TenantId, request?.Subject ?? "", request?.Body ?? "");
            return result.IsSuccess
                ? Results.Ok(new { recipients = result.Data })
                : result.ToHttpResult();
        })
        .WithName("NotifyContacts")
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status500InternalServerError)
        .WithSummary("Email the tenant's contacts")
        .WithDescription("Sends the given subject and plain-text body to every contact of the calling agent's tenant, " +
                         "one email each. Returns how many contacts were emailed (0 when the tenant has no contacts or no email server).");
    }
}
