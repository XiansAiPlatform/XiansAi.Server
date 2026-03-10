using Shared.Auth;
using Shared.Repositories;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
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
    
    /// <summary>
    /// User's role in this tenant: TenantParticipant or TenantParticipantAdmin. Null when tenant matched by domain only.
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

/// <summary>
/// Wrapper response model for participant tenants with system admin status
/// </summary>
public class ParticipantTenantsResponse
{
    [JsonPropertyName("isSystemAdmin")]
    public required bool IsSystemAdmin { get; set; }
    
    [JsonPropertyName("tenants")]
    public required List<ParticipantTenantResponse> Tenants { get; set; }
}

/// <summary>
/// AdminApi endpoints for participant management.
/// These endpoints allow querying participant information across tenants.
/// Restricted to SysAdmin only to prevent cross-tenant information disclosure.
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

        // Get participant by email (tenants + role per tenant)
        participantGroup.MapGet("/{email}", async (
            string email,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ITenantRepository tenantRepository,
            [FromServices] ILogger<IUserRepository> logger) =>
        {
            try
            {
                // Restrict to SysAdmin only - prevents cross-tenant information disclosure
                if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
                {
                    logger.LogWarning("Access denied: Participants endpoint requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                    return Results.Json(
                        new { message = "Access denied: Only system administrators can retrieve participant information across tenants" },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                // Validate and sanitize email input (format, length) before use
                var validatedEmail = ValidationHelpers.SanitizeAndValidateEmail(email);
                if (validatedEmail == null)
                {
                    logger.LogWarning("Invalid email format or length for participant lookup: {Email}", email?.Length > 100 ? "[truncated]" : email);
                    return Results.Json(
                        new { message = "Invalid email address. Email must be well-formed and not exceed 254 characters." },
                        statusCode: StatusCodes.Status400BadRequest);
                }
                email = validatedEmail;
                
                // Extract email domain
                var emailDomain = email.Contains('@') ? email.Split('@')[1] : string.Empty;
                logger.LogInformation("Processing request for email {Email} with domain {Domain}", email, emailDomain);

                // Get user by email
                var user = await userRepository.GetByUserEmailAsync(email);
                
                // Get tenant IDs and role per tenant where user has TenantParticipant or TenantParticipantAdmin (if user exists)
                var participantTenantIds = new List<string>();
                var tenantRoleMap = new Dictionary<string, string>(); // tenantId -> TenantParticipant or TenantParticipantAdmin
                if (user != null)
                {
                    var participantTenantRoles = user.TenantRoles
                        .Where(tr => (tr.Roles.Contains(SystemRoles.TenantParticipant) || tr.Roles.Contains(SystemRoles.TenantParticipantAdmin)) && tr.IsApproved);
                    foreach (var tr in participantTenantRoles)
                    {
                        participantTenantIds.Add(tr.Tenant);
                        // Prefer TenantParticipantAdmin if user has both roles
                        tenantRoleMap[tr.Tenant] = tr.Roles.Contains(SystemRoles.TenantParticipantAdmin)
                            ? SystemRoles.TenantParticipantAdmin
                            : SystemRoles.TenantParticipant;
                    }

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

                // Check if user is system admin
                var isSystemAdmin = user?.IsSysAdmin ?? false;

                var tenantList = matchingTenants
                    .Where(t => t.Enabled)
                    .Select(t => new ParticipantTenantResponse
                    {
                        TenantId = t.TenantId,
                        TenantName = t.Name,
                        Logo = t.Logo,
                        Role = tenantRoleMap.TryGetValue(t.TenantId, out var role) ? role : null
                    })
                    .OrderBy(t => t.TenantName)
                    .ToList();

                // Return 404 if no enabled tenants found
                if (!tenantList.Any())
                {
                    logger.LogWarning("User {Email} has matching tenants but no enabled tenants found. " +
                        "Matching tenant IDs: {TenantIds}, Matched tenants: {MatchedCount}", 
                        email, string.Join(", ", matchingTenants.Select(t => t.TenantId)), matchingTenants.Count);
                    return Results.NotFound(new { message = $"User with email '{email}' has no enabled tenants" });
                }

                var response = new ParticipantTenantsResponse
                {
                    IsSystemAdmin = isSystemAdmin,
                    Tenants = tenantList
                };

                return Results.Ok(response);
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
        .Produces<ParticipantTenantsResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Participant",
            Description = "Retrieve the participant info by email: tenants (with ID, name, logo, and role per tenant) plus system admin status. Tenants are where the user has TenantParticipant or TenantParticipantAdmin role or where the email domain matches the tenant domain. Role is null for domain-matched tenants. **SysAdmin only** - TenantAdmins are not permitted to prevent cross-tenant information disclosure."
        });
    }
}
