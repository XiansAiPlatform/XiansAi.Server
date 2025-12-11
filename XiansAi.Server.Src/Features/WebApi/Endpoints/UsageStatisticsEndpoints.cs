using Features.WebApi.Auth;
using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Data.Models.Usage;
using Shared.Services;
using Features.Shared.Configuration;

namespace Features.WebApi.Endpoints;

public static class UsageStatisticsEndpoints
{
    public static void MapUsageStatisticsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/client/usage/statistics")
            .WithTags("WebAPI - Usage Statistics")
            .RequiresValidTenant()
            .RequireAuthorization()
            .WithGlobalRateLimit();

        // Generic endpoint - handles ALL usage types via query parameter
        group.MapGet("", async (
            [FromQuery] string? type,           // ← NEW: type parameter
            [FromQuery] string? tenantId,
            [FromQuery] string? userId,
            [FromQuery] string? agentName,
            [FromQuery] DateTime? startDate,
            [FromQuery] DateTime? endDate,
            [FromQuery] string? groupBy,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUsageEventService usageStatisticsService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // Validate type parameter
                if (string.IsNullOrWhiteSpace(type))
                {
                    return Results.BadRequest(new { error = "Type parameter is required (tokens, messages, api-calls, etc.)" });
                }

                // Parse type string to enum
                if (!Enum.TryParse<UsageType>(type, ignoreCase: true, out var usageType))
                {
                    var validTypes = string.Join(", ", Enum.GetNames<UsageType>().Select(t => t.ToLower()));
                    return Results.BadRequest(new { error = $"Invalid type '{type}'. Valid types: {validTypes}" });
                }

                return await GetUsageStatisticsInternal(
                    usageType, 
                    tenantId, 
                    userId,
                    agentName,
                    startDate, 
                    endDate, 
                    groupBy, 
                    tenantContext, 
                    usageStatisticsService, 
                    cancellationToken);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException)
            {
                return Results.Forbid();
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
        .WithName("GetUsageStatistics")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get usage statistics (generic)";
            operation.Description = "Retrieves aggregated usage statistics for any type (tokens, messages, api-calls, etc.) with time series data and user breakdown.\n\n" +
                                  "**Query Parameters:**\n" +
                                  "- `type` (required): Usage type - 'tokens', 'messages', 'api-calls', etc.\n" +
                                  "- `startDate` (required): Start of date range (ISO 8601)\n" +
                                  "- `endDate` (required): End of date range (ISO 8601)\n" +
                                  "- `userId` (optional): Filter by user (admins only, defaults to 'all')\n" +
                                  "- `tenantId` (optional): Filter by tenant (SysAdmin only)\n" +
                                  "- `groupBy` (optional): Time grouping - 'day', 'week', 'month' (default: 'day')\n\n" +
                                  "**Authorization:**\n" +
                                  "- Admins can view all users\n" +
                                  "- Regular users can only view their own data\n\n" +
                                  "**Examples:**\n" +
                                  "- `GET /api/client/usage/statistics?type=tokens&startDate=2025-12-01&endDate=2025-12-08`\n" +
                                  "- `GET /api/client/usage/statistics?type=messages&startDate=2025-12-01&endDate=2025-12-08&userId=user123`";
            return operation;
        });

        // Get users with usage (admin only)
        group.MapGet("/users", async (
            [FromQuery] string? tenantId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUsageEventService usageStatisticsService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // Only admins can list users
                if (!IsAdmin(tenantContext))
                {
                    return Results.Forbid();
                }

                // Apply security: determine effective tenant
                var effectiveTenantId = DetermineEffectiveTenantId(tenantContext, tenantId);

                var users = await usageStatisticsService.GetUsersWithUsageAsync(effectiveTenantId, cancellationToken);
                return Results.Ok(new { users });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
        .WithName("GetUsersWithUsage")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get users with usage data";
            operation.Description = "Returns list of users that have token/message usage in the tenant. " +
                                  "Admin only endpoint - used for populating user filter dropdown.";
            return operation;
        });
    }

    /// <summary>
    /// Determines the effective tenant ID based on user role and request.
    /// SysAdmin can specify any tenant, others use their own tenant.
    /// </summary>
    private static string DetermineEffectiveTenantId(ITenantContext context, string? requestedTenantId)
    {
        // SysAdmin can specify any tenant
        if (context.UserRoles.Contains(SystemRoles.SysAdmin) && !string.IsNullOrWhiteSpace(requestedTenantId))
        {
            return requestedTenantId;
        }

        // All others use their own tenant
        return context.TenantId;
    }

    /// <summary>
    /// Determines the effective user ID based on user role and request.
    /// Regular users are forced to their own user ID.
    /// Admins can specify "all" or a specific user.
    /// </summary>
    private static string? DetermineEffectiveUserId(ITenantContext context, string? requestedUserId)
    {
        // Regular users must use their own user ID
        if (!IsAdmin(context))
        {
            return context.LoggedInUser;
        }

        // Admins can view all users or a specific user
        if (string.IsNullOrWhiteSpace(requestedUserId) || requestedUserId == "all")
        {
            return null; // null means "all users"
        }

        return requestedUserId;
    }

    /// <summary>
    /// Validates that the user has permission to view the requested data.
    /// </summary>
    private static void ValidateUserAccess(ITenantContext context, string? effectiveUserId)
    {
        // SysAdmin and TenantAdmin can access any user
        if (IsAdmin(context))
        {
            return;
        }

        // Regular users can only access their own data
        if (!string.IsNullOrEmpty(effectiveUserId) && effectiveUserId != context.LoggedInUser)
        {
            throw new UnauthorizedAccessException("You can only view your own usage data");
        }
    }

    /// <summary>
    /// Checks if the user is an admin (SysAdmin or TenantAdmin).
    /// </summary>
    private static bool IsAdmin(ITenantContext context)
    {
        return context.UserRoles.Contains(SystemRoles.SysAdmin) || 
               context.UserRoles.Contains(SystemRoles.TenantAdmin);
    }

    /// <summary>
    /// Internal helper method to get usage statistics - shared logic for all types.
    /// </summary>
    private static async Task<IResult> GetUsageStatisticsInternal(
        UsageType type,
        string? tenantId,
        string? userId,
        string? agentName,
        DateTime? startDate,
        DateTime? endDate,
        string? groupBy,
        ITenantContext tenantContext,
        IUsageEventService usageStatisticsService,
        CancellationToken cancellationToken)
    {
        // Validate required parameters
        if (!startDate.HasValue || !endDate.HasValue)
        {
            return Results.BadRequest(new { error = "StartDate and EndDate are required" });
        }

        // Apply security: determine effective tenant and user
        var effectiveTenantId = DetermineEffectiveTenantId(tenantContext, tenantId);
        var effectiveUserId = DetermineEffectiveUserId(tenantContext, userId);

        // Validate user access
        ValidateUserAccess(tenantContext, effectiveUserId);

        var request = new UsageEventsRequest
        {
            TenantId = effectiveTenantId,
            UserId = effectiveUserId,
            AgentName = string.IsNullOrWhiteSpace(agentName) || agentName == "all" ? null : agentName,
            Type = type,
            StartDate = startDate.Value,
            EndDate = endDate.Value,
            GroupBy = groupBy ?? "day"
        };

        var response = await usageStatisticsService.GetUsageEventsAsync(request, cancellationToken);
        return Results.Ok(response);
    }
}

