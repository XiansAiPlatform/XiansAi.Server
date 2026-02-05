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

        // Flexible endpoint - supports category and metric type filtering
        group.MapGet("", async (
            [FromQuery] string? category,       // Metric category (tokens, activity, performance)
            [FromQuery] string? metricType,     // Specific metric type
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
                    ParticipantId = effectiveUserId,
                    AgentName = string.IsNullOrWhiteSpace(agentName) || agentName == "all" ? null : agentName,
                    Category = category,
                    MetricType = metricType,
                    StartDate = startDate.Value,
                    EndDate = endDate.Value,
                    GroupBy = groupBy ?? "day"
                };

                var response = await usageStatisticsService.GetUsageEventsAsync(request, cancellationToken);
                return Results.Ok(response);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(
                    new { error = "Forbidden", message = ex.Message },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message);
            }
        })
        .WithName("GetUsageStatistics")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get usage statistics (flexible)";
            operation.Description = "Retrieves aggregated usage statistics filtered by category and/or metric type with time series data and user breakdown.\n\n" +
                                  "**Query Parameters:**\n" +
                                  "- `category` (optional): Metric category - 'tokens', 'activity', 'performance', 'llm_usage'\n" +
                                  "- `metricType` (optional): Specific metric type (e.g., 'total_tokens', 'workflow_completed')\n" +
                                  "- `startDate` (required): Start of date range (ISO 8601)\n" +
                                  "- `endDate` (required): End of date range (ISO 8601)\n" +
                                  "- `userId` (optional): Filter by user (admins only, defaults to 'all')\n" +
                                  "- `agentName` (optional): Filter by agent name\n" +
                                  "- `tenantId` (optional): Filter by tenant (SysAdmin only)\n" +
                                  "- `groupBy` (optional): Time grouping - 'day', 'week', 'month' (default: 'day')\n\n" +
                                  "**Authorization:**\n" +
                                  "- Admins can view all users\n" +
                                  "- Regular users can only view their own data\n\n" +
                                  "**Examples:**\n" +
                                  "- `GET /api/client/usage/statistics?category=tokens&metricType=total_tokens&startDate=2025-01-01&endDate=2025-01-08`\n" +
                                  "- `GET /api/client/usage/statistics?category=activity&startDate=2025-01-01&endDate=2025-01-08&agentName=EmailAgent`";
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
                    return Results.Json(
                        new { error = "Forbidden", message = "You do not have permission to list users" },
                        statusCode: StatusCodes.Status403Forbidden);
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

        // Get available metrics (discovery endpoint for dynamic dashboard)
        group.MapGet("/available-metrics", async (
            [FromQuery] string? tenantId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IUsageEventService usageStatisticsService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                // Apply security: determine effective tenant
                var effectiveTenantId = DetermineEffectiveTenantId(tenantContext, tenantId);

                var metrics = await usageStatisticsService.GetAvailableMetricsAsync(effectiveTenantId, cancellationToken);
                return Results.Ok(metrics);
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
        .WithName("GetAvailableMetrics")
        .WithOpenApi(operation =>
        {
            operation.Summary = "Get available metrics for dynamic dashboard";
            operation.Description = "Returns all available metric categories and types that have data for the tenant. " +
                                  "Used by the UI to dynamically build the metrics selection interface.\n\n" +
                                  "**Response:**\n" +
                                  "- categories: Array of metric categories\n" +
                                  "  - categoryId: Category identifier (e.g., 'tokens', 'activity')\n" +
                                  "  - categoryName: Display name\n" +
                                  "  - metrics: Array of metric definitions\n" +
                                  "    - type: Metric type identifier\n" +
                                  "    - displayName: Human-readable name\n" +
                                  "    - unit: Measurement unit\n" +
                                  "    - description: Optional metric description";
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
}

