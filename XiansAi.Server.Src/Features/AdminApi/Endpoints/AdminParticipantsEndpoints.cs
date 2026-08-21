using Shared.Auth;
using Shared.Repositories;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using Shared.Utils;
using Features.AdminApi.Utils;

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
            HttpContext httpContext,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ITenantRepository tenantRepository,
            [FromServices] LinkGenerator linkGenerator,
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

                // An address can answer to more than one record — the same person arriving through
                // two directories is legitimate — and this reply names no account, only the tenants
                // the person reaches and their role in each. So the records are combined rather
                // than one being picked or the request refused, which would leave the person with
                // no tenants at all and no way for an operator to resolve it.
                var records = await userRepository.GetAllByUserEmailAsync(email);
                var subject = new ParticipantSubject("email", email, LogSanitizer.RedactEmail(email));

                if (RefuseWhenEveryRecordIsDisabled(records, subject, logger, out var disabled))
                {
                    return disabled;
                }

                var owners = EmailIdentityResolution.UsableRecords(records);
                if (owners.Count > 1)
                {
                    logger.LogInformation("Participant {Subject} resolves to {Count} accounts; combining them",
                        subject.Redacted, owners.Count);
                }

                return await BuildParticipantTenantsAsync(
                    owners, subject, httpContext, tenantRepository, linkGenerator, logger);
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

        // Same reply, keyed on the account rather than on an address. Preferred by callers that
        // hold the signed-in person's provider subject, because it names one account outright:
        // nothing is combined, so what comes back is what that account alone can reach. The
        // address route stays for callers that have no more than an address.
        participantGroup.MapGet("/by-user-id/{userId}", async (
            string userId,
            HttpContext httpContext,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUserRepository userRepository,
            [FromServices] ITenantRepository tenantRepository,
            [FromServices] LinkGenerator linkGenerator,
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

                if (string.IsNullOrWhiteSpace(userId) || userId.Length > MaxUserIdLength)
                {
                    logger.LogWarning("Invalid user id for participant lookup: {Length} characters", userId?.Length ?? 0);
                    return Results.Problem(
                        detail: $"Invalid user id. It must not be empty or exceed {MaxUserIdLength} characters.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var user = await userRepository.GetByUserIdAsync(userId);
                var records = user == null ? new List<User>() : new List<User> { user };
                var subject = new ParticipantSubject("id", userId, LogSanitizer.RedactUserId(userId));

                if (RefuseWhenEveryRecordIsDisabled(records, subject, logger, out var disabled))
                {
                    return disabled;
                }

                return await BuildParticipantTenantsAsync(
                    EmailIdentityResolution.UsableRecords(records), subject,
                    httpContext, tenantRepository, linkGenerator, logger);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error retrieving participant tenants for {UserId}", LogSanitizer.RedactUserId(userId));
                return Results.Problem(
                    detail: "An error occurred while retrieving participant tenants",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        })
        .WithName("GetParticipantTenantsByUserId")
        .Produces<ParticipantTenantsResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status500InternalServerError)
        ;
    }

    private const int MaxUserIdLength = 200;

    /// <summary>
    /// How a participant was named, so that the reply and the log can each say so appropriately.
    /// The caller supplied <paramref name="Value"/>, so echoing it back discloses nothing, while
    /// the log is read by people who did not supply it and gets <paramref name="Redacted"/>.
    /// </summary>
    /// <param name="Kind">"email" or "id", as it appears in the not-found messages.</param>
    /// <param name="Value">The identifier as the caller gave it.</param>
    /// <param name="Redacted">The same identifier, safe to log.</param>
    private readonly record struct ParticipantSubject(string Kind, string Value, string Redacted);

    /// <summary>
    /// Refuses when records exist for the participant but every one of them is disabled.
    ///
    /// A disabled duplicate awaiting review is dropped rather than counted, so it cannot shut out
    /// the live account beside it. Only when nothing is left is the person themselves refused.
    /// </summary>
    private static bool RefuseWhenEveryRecordIsDisabled(
        IReadOnlyList<User> records, ParticipantSubject subject, ILogger logger, out IResult refusal)
    {
        if (records.Count == 0 || EmailIdentityResolution.UsableRecords(records).Count > 0)
        {
            refusal = Results.Empty;
            return false;
        }

        logger.LogWarning("Participant lookup denied: every account for {Subject} is locked out", subject.Redacted);
        refusal = Results.Problem(
            detail: "Access denied: the user account is locked out",
            statusCode: StatusCodes.Status403Forbidden);
        return true;
    }

    /// <summary>
    /// Builds the reply from the records that answer for one participant: the tenants they reach
    /// and their highest role in each.
    ///
    /// Takes a list rather than one record because an address can answer to several — the same
    /// person arriving through two directories — and this reply names no account, so combining them
    /// is the right answer where picking one would not be. A caller naming an account outright
    /// passes the one record and the combining steps are no-ops.
    /// </summary>
    private static async Task<IResult> BuildParticipantTenantsAsync(
        IReadOnlyList<User> owners,
        ParticipantSubject subject,
        HttpContext httpContext,
        ITenantRepository tenantRepository,
        LinkGenerator linkGenerator,
        ILogger logger)
    {
        // Tenant IDs and highest-privilege role per tenant, for all memberships (approved or not).
        // Roles for a tenant are combined across the records first, so someone who is an
        // administrator through one is not reported as a participant because the other says less.
        var participantTenantIds = new List<string>();
        var rolesByTenant = new Dictionary<string, List<string>>();
        foreach (var owner in owners)
        {
            foreach (var membership in owner.TenantRoles)
            {
                if (!rolesByTenant.TryGetValue(membership.Tenant, out var combinedRoles))
                {
                    combinedRoles = new List<string>();
                    rolesByTenant[membership.Tenant] = combinedRoles;
                    participantTenantIds.Add(membership.Tenant);
                }

                combinedRoles.AddRange(membership.Roles);
            }
        }

        var tenantRoleMap = rolesByTenant.ToDictionary(entry => entry.Key, entry => PrimaryRole(entry.Value));

        if (owners.Count > 0)
        {
            logger.LogInformation("User {Subject} has roles in {Count} tenants: {TenantIds}",
                subject.Redacted, participantTenantIds.Count, string.Join(", ", participantTenantIds));
        }
        else
        {
            logger.LogInformation("User {Subject} not found", subject.Redacted);
        }

        // Granted only when every record answering for the participant holds it: the role is
        // global, so one record short means nobody has yet accepted that the records are one
        // person. Where a single record answers, that is simply its own flag.
        var isSystemAdmin = owners.Count > 0
            && EmailIdentityResolution.ResolveSysAdmin(subject.Redacted, owners, logger);

        // System admins have access to all tenants, regardless of explicit memberships.
        // Other users' tenants are derived strictly from their explicit memberships (TenantRoles).
        // Email-domain matching is intentionally not used to grant tenant access or roles.
        var matchingTenants = new List<Tenant>();
        if (isSystemAdmin)
        {
            matchingTenants = await tenantRepository.GetAllAsync();
            logger.LogInformation("User {Subject} is a system admin. Returning all {Count} tenants.",
                subject.Redacted, matchingTenants.Count);
        }
        else if (participantTenantIds.Any())
        {
            matchingTenants = await tenantRepository.GetByTenantIdsAsync(participantTenantIds);
            logger.LogInformation("Found {Count} tenants from roles: {TenantIds}",
                matchingTenants.Count, string.Join(", ", matchingTenants.Select(t => t.TenantId)));
        }

        if (!matchingTenants.Any())
        {
            return Results.NotFound(new { message = $"User with {subject.Kind} '{subject.Value}' has no matching tenants" });
        }

        logger.LogInformation("Matched {Count} tenants by TenantId. Tenants: {Tenants}",
            matchingTenants.Count,
            string.Join(", ", matchingTenants.Select(t => $"{t.TenantId}(enabled:{t.Enabled})")));

        // Default role for tenants where the user has no explicit membership.
        // System admins implicitly have access to every tenant via their SysAdmin role.
        var defaultRole = isSystemAdmin ? SystemRoles.SysAdmin : SystemRoles.TenantParticipant;

        var tenantList = matchingTenants
            .Where(t => t.Enabled)
            .Select(t => new ParticipantTenantResponse
            {
                TenantId = t.TenantId,
                TenantName = t.Name,
                Logo = TenantLogoHelper.BuildLogoResponse(t, httpContext, linkGenerator),
                Role = tenantRoleMap.TryGetValue(t.TenantId, out var role) ? role : defaultRole,
                Theme = t.Theme
            })
            .OrderBy(t => t.TenantName)
            .ToList();

        // Return 404 if no enabled tenants found
        if (!tenantList.Any())
        {
            logger.LogWarning("User {Subject} has matching tenants but no enabled tenants found. " +
                "Matching tenant IDs: {TenantIds}, Matched tenants: {MatchedCount}",
                subject.Redacted, string.Join(", ", matchingTenants.Select(t => t.TenantId)), matchingTenants.Count);
            return Results.NotFound(new { message = $"User with {subject.Kind} '{subject.Value}' has no enabled tenants" });
        }

        return Results.Ok(new ParticipantTenantsResponse
        {
            IsSystemAdmin = isSystemAdmin,
            Tenants = tenantList
        });
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
