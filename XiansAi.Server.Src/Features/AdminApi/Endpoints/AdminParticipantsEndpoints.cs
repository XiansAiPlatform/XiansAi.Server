using Shared.Auth;
using Shared.Repositories;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using Shared.Utils;

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
    
    /// <summary>
    /// User's highest-privilege role in this tenant, derived from their explicit tenant membership.
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    /// <summary>
    /// Optional UI color theme for this tenant (e.g. "lingon", "fjord", "skog", "zenith").
    /// When set, the studio applies this as the default theme for the tenant.
    /// </summary>
    [JsonPropertyName("theme")]
    public string? Theme { get; set; }
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
                    return Results.Problem(
                        detail: "Access denied: Only system administrators can retrieve participant information across tenants",
                        statusCode: StatusCodes.Status403Forbidden);
                }

                // Validate and sanitize email input (format, length) before use
                var validatedEmail = ValidationHelpers.SanitizeAndValidateEmail(email);
                if (validatedEmail == null)
                {
                    logger.LogWarning("Invalid email format or length for participant lookup: {EmailRedacted}", LogSanitizer.RedactEmail(email));
                    return Results.Problem(
                        detail: "Invalid email address. Email must be well-formed and not exceed 254 characters.",
                        statusCode: StatusCodes.Status400BadRequest);
                }
                email = validatedEmail;

                // Get user by email
                var user = await userRepository.GetByUserEmailAsync(email);

                // Reject locked-out users
                if (user?.IsLockedOut == true)
                {
                    logger.LogWarning("Participant lookup denied: user {EmailRedacted} is locked out", LogSanitizer.RedactEmail(email));
                    return Results.Problem(
                        detail: "Access denied: the user account is locked out",
                        statusCode: StatusCodes.Status403Forbidden);
                }

                // Get tenant IDs and highest-privilege role per tenant for all memberships (approved or not)
                var participantTenantIds = new List<string>();
                var tenantRoleMap = new Dictionary<string, string>(); // tenantId -> highest role
                if (user != null)
                {
                    foreach (var tr in user.TenantRoles)
                    {
                        participantTenantIds.Add(tr.Tenant);
                        tenantRoleMap[tr.Tenant] = PrimaryRole(tr.Roles);
                    }

                    logger.LogInformation("User {EmailRedacted} has roles in {Count} tenants: {TenantIds}", 
                        LogSanitizer.RedactEmail(email), participantTenantIds.Count, string.Join(", ", participantTenantIds));
                }
                else
                {
                    logger.LogInformation("User {EmailRedacted} not found", LogSanitizer.RedactEmail(email));
                }

                // Tenants are derived strictly from the user's explicit memberships (TenantRoles).
                // Email-domain matching is intentionally not used to grant tenant access or roles.
                var matchingTenants = new List<Tenant>();
                if (participantTenantIds.Any())
                {
                    matchingTenants = await tenantRepository.GetByTenantIdsAsync(participantTenantIds);
                    logger.LogInformation("Found {Count} tenants from roles: {TenantIds}", 
                        matchingTenants.Count, string.Join(", ", matchingTenants.Select(t => t.TenantId)));
                }

                if (!matchingTenants.Any())
                {
                    return Results.NotFound(new { message = $"User with email '{email}' has no matching tenants" });
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
                        Role = tenantRoleMap.TryGetValue(t.TenantId, out var role) ? role : SystemRoles.TenantParticipant,
                        Theme = t.Theme
                    })
                    .OrderBy(t => t.TenantName)
                    .ToList();

                // Return 404 if no enabled tenants found
                if (!tenantList.Any())
                {
                    logger.LogWarning("User {EmailRedacted} has matching tenants but no enabled tenants found. " +
                        "Matching tenant IDs: {TenantIds}, Matched tenants: {MatchedCount}", 
                        LogSanitizer.RedactEmail(email), string.Join(", ", matchingTenants.Select(t => t.TenantId)), matchingTenants.Count);
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
                logger.LogError(ex, "Error retrieving participant tenants for {EmailRedacted}", LogSanitizer.RedactEmail(email));
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
        ;
    }

    /// <summary>
    /// Returns the highest-privilege role from the list, falling back to TenantParticipant.
    /// Priority: TenantAdmin → TenantUser → TenantParticipantAdmin → TenantParticipant.
    /// </summary>
    private static string PrimaryRole(List<string> roles)
    {
        string[] priority = { SystemRoles.TenantAdmin, SystemRoles.TenantUser, SystemRoles.TenantParticipantAdmin, SystemRoles.TenantParticipant };
        foreach (var candidate in priority)
        {
            if (roles.Contains(candidate))
                return candidate;
        }
        return roles.FirstOrDefault() ?? SystemRoles.TenantParticipant;
    }
}
