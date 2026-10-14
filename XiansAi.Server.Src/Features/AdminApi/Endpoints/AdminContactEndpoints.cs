using Features.AdminApi.Auth;
using Features.AdminApi.Constants;
using Microsoft.AspNetCore.Mvc;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi CRUD for tenant-scoped notification contacts.
/// Business logic lives in <see cref="IContactService"/>; these endpoints only delegate.
/// </summary>
public static class AdminContactEndpoints
{
    public sealed record ContactRequest(string? Name, string? Email, string? Phone);

    public static void MapAdminContactEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var group = adminApiGroup.MapGroup("/tenants/{tenantId}/contacts")
            .WithTags("AdminAPI - Contacts")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>()
            .EnforceCapabilities();

        group.MapGet("", async (
            string tenantId,
            [FromQuery] string? search,
            [FromServices] IContactService service) =>
        {
            var result = await service.ListAsync(tenantId, search);
            return result.ToHttpResult();
        })
        .WithName("AdminListContacts")
        .RequireCapability(CapabilityActions.TenantContactsList);

        group.MapGet("/{id}", async (
            string tenantId,
            string id,
            [FromServices] IContactService service) =>
        {
            var result = await service.GetAsync(tenantId, id);
            return result.ToHttpResult();
        })
        .WithName("AdminGetContact")
        .RequireCapability(CapabilityActions.TenantContactsGet);

        group.MapPost("", async (
            string tenantId,
            [FromBody] ContactRequest body,
            [FromServices] IContactService service) =>
        {
            var result = await service.CreateAsync(tenantId, new ContactInput(body.Name, body.Email, body.Phone));
            if (result.IsSuccess && result.Data != null)
            {
                var location = AdminApiConstants.BuildVersionedPath(
                    $"tenants/{Uri.EscapeDataString(tenantId)}/contacts/{result.Data.Id}");
                return Results.Created(location, result.Data);
            }
            return result.ToHttpResult();
        })
        .WithName("AdminCreateContact")
        .RequireCapability(CapabilityActions.TenantContactsCreate);

        group.MapPatch("/{id}", async (
            string tenantId,
            string id,
            [FromBody] ContactRequest body,
            [FromServices] IContactService service) =>
        {
            var result = await service.UpdateAsync(tenantId, id, new ContactInput(body.Name, body.Email, body.Phone));
            return result.ToHttpResult();
        })
        .WithName("AdminUpdateContact")
        .RequireCapability(CapabilityActions.TenantContactsUpdate);

        group.MapDelete("/{id}", async (
            string tenantId,
            string id,
            [FromServices] IContactService service) =>
        {
            var result = await service.DeleteAsync(tenantId, id);
            return result.IsSuccess ? Results.NoContent() : result.ToHttpResult();
        })
        .WithName("AdminDeleteContact")
        .RequireCapability(CapabilityActions.TenantContactsDelete);
    }
}
