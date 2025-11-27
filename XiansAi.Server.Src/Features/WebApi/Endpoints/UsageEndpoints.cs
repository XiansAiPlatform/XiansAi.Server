using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Features.WebApi.Auth;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.WebApi.Endpoints;

public static class UsageEndpoints
{
    public static void MapUsageEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/client/usage")
            .WithTags("WebAPI - Usage")
            .RequiresValidTenant()
            .RequireAuthorization();

        group.MapGet("/status", async (
            [FromQuery] string? tenantId,
            [FromQuery] string? userId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITokenUsageAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var resolvedTenant = ResolveTenantId(tenantContext, tenantId);
                var resolvedUser = ResolveUserId(tenantContext, userId);

                var result = await adminService.GetStatusAsync(resolvedTenant, resolvedUser, cancellationToken);
                return result.ToHttpResult();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).RequiresValidTenantAdmin()
          .WithOpenApi(operation =>
          {
              operation.Summary = "Get token usage status";
              operation.Description = "Returns token usage status for the current tenant or specified tenant/user (sys-admin only).";
              return operation;
          });

        group.MapGet("/limits", async (
            [FromQuery] string? tenantId,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITokenUsageAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var resolvedTenant = ResolveTenantId(tenantContext, tenantId);
                var result = await adminService.GetLimitsAsync(resolvedTenant, cancellationToken);
                return result.ToHttpResult();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).RequiresValidTenantAdmin()
          .WithOpenApi(operation =>
          {
              operation.Summary = "List token usage limits";
              operation.Description = "Lists tenant-level and user-level token usage limits.";
              return operation;
          });

        group.MapPost("/limits", async (
            [FromBody] TokenUsageLimitRequest request,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITokenUsageAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            if (request == null)
            {
                return Results.BadRequest("Request body is required.");
            }

            try
            {
                var resolvedTenant = ResolveTenantId(tenantContext, request.TenantId);
                var updatedBy = tenantContext.LoggedInUser ?? "system";

                var serviceResult = await adminService.UpsertLimitAsync(new UpsertTokenUsageLimitRequest(
                    TenantId: resolvedTenant,
                    UserId: request.UserId,
                    MaxTokens: request.MaxTokens,
                    WindowSeconds: request.WindowSeconds,
                    Enabled: request.Enabled,
                    UpdatedBy: updatedBy), cancellationToken);

                return serviceResult.ToHttpResult();
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
            catch (InvalidOperationException ex)
            {
                return Results.BadRequest(ex.Message);
            }
        }).RequiresValidTenantAdmin()
          .WithOpenApi(operation =>
          {
              operation.Summary = "Create or update a token usage limit";
              operation.Description = "Creates or updates a tenant-level or user-level token usage limit.";
              return operation;
          });

        group.MapDelete("/limits/{id}", async (
            string id,
            [FromServices] ITokenUsageAdminService adminService,
            CancellationToken cancellationToken) =>
        {
            var result = await adminService.DeleteLimitAsync(id, cancellationToken);
            return result.ToHttpResult();
        }).RequiresValidTenantAdmin()
          .WithOpenApi(operation =>
          {
              operation.Summary = "Delete a token usage limit";
              operation.Description = "Deletes a token usage limit by its identifier.";
              return operation;
          });
    }

    private static string ResolveTenantId(ITenantContext context, string? requestedTenantId)
    {
        if (!string.IsNullOrWhiteSpace(requestedTenantId))
        {
            if (context.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                return requestedTenantId;
            }

            if (!string.Equals(context.TenantId, requestedTenantId, StringComparison.OrdinalIgnoreCase))
            {
                throw new UnauthorizedAccessException("You are not authorized to manage the specified tenant.");
            }

            return requestedTenantId;
        }

        if (string.IsNullOrWhiteSpace(context.TenantId))
        {
            throw new InvalidOperationException("Tenant context is required.");
        }

        return context.TenantId;
    }

    private static string ResolveUserId(ITenantContext context, string? requestedUserId)
    {
        if (!string.IsNullOrWhiteSpace(requestedUserId))
        {
            return requestedUserId;
        }

        if (!string.IsNullOrWhiteSpace(context.LoggedInUser))
        {
            return context.LoggedInUser;
        }

        return context.TenantId ?? throw new InvalidOperationException("Unable to determine user context.");
    }

    private sealed class TokenUsageLimitRequest
    {
        public string? TenantId { get; set; }
        public string? UserId { get; set; }
        public long MaxTokens { get; set; }
        public int WindowSeconds { get; set; }
        public bool Enabled { get; set; } = true;
    }
}

