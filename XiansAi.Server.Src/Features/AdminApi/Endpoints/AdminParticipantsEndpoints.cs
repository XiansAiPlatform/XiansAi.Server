using Shared.Repositories;
using Shared.Data.Models;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// Comparer for Tenant objects that compares by TenantId
/// </summary>
internal class TenantIdComparer : IEqualityComparer<Tenant>
{
    public bool Equals(Tenant? x, Tenant? y)
    {
        if (x == null && y == null) return true;
        if (x == null || y == null) return false;
        return x.TenantId == y.TenantId;
    }

    public int GetHashCode(Tenant obj)
    {
        return obj.TenantId.GetHashCode();
    }
}

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
                // Normalize email to lowercase for case-insensitive comparison
                email = email.ToLowerInvariant();
                
                // Extract email domain
                var emailDomain = email.Contains('@') ? email.Split('@')[1] : string.Empty;
                logger.LogInformation("Processing request for email {Email} with domain {Domain}", email, emailDomain);

                // Get user by email
                var user = await userRepository.GetByUserEmailAsync(email);
                
                // Get tenant IDs where user has TenantParticipant role (if user exists)
                var participantTenantIds = new List<string>();
                if (user != null)
                {
                    participantTenantIds = user.TenantRoles
                        .Where(tr => tr.Roles.Contains(SystemRoles.TenantParticipant) && tr.IsApproved)
                        .Select(tr => tr.Tenant)
                        .ToList();

                    logger.LogInformation("User {Email} has participant roles in {Count} tenants: {TenantIds}", 
                        email, participantTenantIds.Count, string.Join(", ", participantTenantIds));
                }
                else
                {
                    logger.LogInformation("User with email {Email} not found, will check domain matching only", email);
                }

                // Query tenants by domain matching (if email has domain)
                var domainMatchedTenants = new List<Tenant>();
                if (!string.IsNullOrEmpty(emailDomain))
                {
                    domainMatchedTenants = await tenantRepository.GetByDomainListAsync(emailDomain);
                    logger.LogInformation("Found {Count} tenants matching email domain {Domain}: {TenantIds}", 
                        domainMatchedTenants.Count, emailDomain, string.Join(", ", domainMatchedTenants.Select(t => t.TenantId)));
                }

                // Query tenants by participant role tenant IDs (if user has participant roles)
                var participantTenants = new List<Tenant>();
                if (participantTenantIds.Any())
                {
                    participantTenants = await tenantRepository.GetByTenantIdsAsync(participantTenantIds);
                    logger.LogInformation("Found {Count} tenants from participant roles: {TenantIds}", 
                        participantTenants.Count, string.Join(", ", participantTenants.Select(t => t.TenantId)));
                }

                // Combine both lists and remove duplicates
                var matchingTenants = participantTenants
                    .Union(domainMatchedTenants, new TenantIdComparer())
                    .ToList();

                logger.LogInformation("Total matching tenants: {Count} ({ParticipantCount} from roles, {DomainCount} from domain): {TenantIds}",
                    matchingTenants.Count, participantTenants.Count, domainMatchedTenants.Count, 
                    string.Join(", ", matchingTenants.Select(t => t.TenantId)));

                if (!matchingTenants.Any())
                {
                    return Results.NotFound(new { message = $"User with email '{email}' has no matching tenants (neither participant roles nor domain match)" });
                }
                
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
                    logger.LogWarning("User {Email} has matching tenants but no enabled tenants found. " +
                        "Matching tenant IDs: {TenantIds}, Matched tenants: {MatchedCount}", 
                        email, string.Join(", ", matchingTenants.Select(t => t.TenantId)), matchingTenants.Count);
                    return Results.NotFound(new { message = $"User with email '{email}' has no enabled tenants" });
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
            Description = "Retrieve the list of tenants (with ID and name) where the user with the specified email has the TenantParticipant role or where the email domain matches the tenant domain, and the tenant is enabled."
        });
    }
}
