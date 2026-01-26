using Shared.Services;
using Shared.Repositories;
using Shared.Auth;
using Shared.Data.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Shared.Utils.Services;
using System.Text.Json.Serialization;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// Response model for participant tenant information
/// </summary>
public class ParticipantTenantResponse
{
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; set; }
    
    [JsonPropertyName("tenantName")]
    public required string TenantName { get; set; }
    
    [JsonPropertyName("logo")]
    public Logo? Logo { get; set; }
}

/// <summary>
/// AdminApi endpoints for participant management.
/// These endpoints allow querying participant information across tenants.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminParticipantsEndpoints
{
    /// <summary>
    /// Maps all AdminApi participant endpoints.
    /// </summary>
    public static void MapAdminParticipantsEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var participantGroup = adminApiGroup.MapGroup("/participants")
            .WithTags("AdminAPI - Participants")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Get participant tenants by email
        participantGroup.MapGet("/{email}/tenants", async (
            string email,
            [FromServices] IUserRepository userRepository,
            [FromServices] ITenantRepository tenantRepository,
            [FromServices] ILogger<IUserRepository> logger) =>
        {
            try
            {
                // Get user by email
                var user = await userRepository.GetByUserEmailAsync(email);
                if (user == null)
                {
                    return Results.NotFound(new { message = $"Participant with email '{email}' not found" });
                }

                // Get tenant IDs where user has TenantParticipant role
                var participantTenantIds = user.TenantRoles
                    .Where(tr => tr.Roles.Contains(SystemRoles.TenantParticipant) && tr.IsApproved)
                    .Select(tr => tr.Tenant)
                    .ToList();

                logger.LogInformation("User {Email} has participant roles in {Count} tenants: {TenantIds}", 
                    email, participantTenantIds.Count, string.Join(", ", participantTenantIds));

                if (!participantTenantIds.Any())
                {
                    return Results.NotFound(new { message = $"User with email '{email}' has no participant tenants" });
                }

                // Get all tenants
                var allTenants = await tenantRepository.GetAllAsync();
                logger.LogInformation("Found {Count} total tenants in the system", allTenants.Count);

                // Filter to enabled tenants where user is a participant
                var matchingTenants = allTenants
                    .Where(t => participantTenantIds.Contains(t.TenantId))
                    .ToList();
                
                logger.LogInformation("Matched {Count} tenants by TenantId. Tenants: {Tenants}", 
                    matchingTenants.Count, 
                    string.Join(", ", matchingTenants.Select(t => $"{t.TenantId}(enabled:{t.Enabled})")));

                var tenantResponse = matchingTenants
                    .Where(t => t.Enabled)
                    .Select(t => new ParticipantTenantResponse
                    {
                        TenantId = t.TenantId,
                        TenantName = t.Name,
                        Logo = t.Logo
                    })
                    .OrderBy(t => t.TenantName)
                    .ToList();

                // Return 404 if no enabled tenants found
                if (!tenantResponse.Any())
                {
                    logger.LogWarning("User {Email} has participant roles but no enabled tenants found. " +
                        "Participant tenant IDs: {TenantIds}, Matched tenants: {MatchedCount}", 
                        email, string.Join(", ", participantTenantIds), matchingTenants.Count);
                    return Results.NotFound(new { message = $"User with email '{email}' has no enabled participant tenants" });
                }

                return Results.Ok(tenantResponse);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving participant tenants for email {Email}", email);
                return Results.Problem(
                    detail: "An error occurred while retrieving participant tenants",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("GetParticipantTenants")
        .Produces<List<ParticipantTenantResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Participant Tenants",
            Description = "Retrieve the list of tenants (with ID and name) where the user with the specified email has the TenantParticipant role and the tenant is enabled."
        });
    }
}
