using Shared.Repositories;
using Shared.Data.Models;
using Microsoft.AspNetCore.Mvc;
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
                // Extract email domain
                var emailDomain = email.Contains('@') ? email.Split('@')[1].ToLowerInvariant() : string.Empty;
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

                // Get all tenants to check both participant roles and domain matching
                var allTenants = await tenantRepository.GetAllAsync();
                logger.LogInformation("Found {Count} total tenants in the system", allTenants.Count);

                // Find tenants where email domain matches tenant domain
                var domainMatchedTenants = new List<string>();
                if (!string.IsNullOrEmpty(emailDomain))
                {
                    domainMatchedTenants = allTenants
                        .Where(t => !string.IsNullOrEmpty(t.Domain) && 
                                   t.Domain.ToLowerInvariant() == emailDomain)
                        .Select(t => t.TenantId)
                        .ToList();
                    
                    logger.LogInformation("Found {Count} tenants matching email domain {Domain}: {TenantIds}", 
                        domainMatchedTenants.Count, emailDomain, string.Join(", ", domainMatchedTenants));
                }

                // Combine both lists and remove duplicates
                var allMatchingTenantIds = participantTenantIds
                    .Union(domainMatchedTenants)
                    .ToList();

                logger.LogInformation("Total matching tenant IDs: {Count} ({ParticipantCount} from roles, {DomainCount} from domain): {TenantIds}",
                    allMatchingTenantIds.Count, participantTenantIds.Count, domainMatchedTenants.Count, 
                    string.Join(", ", allMatchingTenantIds));

                if (!allMatchingTenantIds.Any())
                {
                    return Results.NotFound(new { message = $"User with email '{email}' has no matching tenants (neither participant roles nor domain match)" });
                }

                // Filter to matching tenants
                var matchingTenants = allTenants
                    .Where(t => allMatchingTenantIds.Contains(t.TenantId))
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
                    logger.LogWarning("User {Email} has matching tenants but no enabled tenants found. " +
                        "Matching tenant IDs: {TenantIds}, Matched tenants: {MatchedCount}", 
                        email, string.Join(", ", allMatchingTenantIds), matchingTenants.Count);
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
