using Shared.Repositories;
using Shared.Utils.Services;
using Shared.Auth;
using Microsoft.Extensions.Logging;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Features.AdminApi.Utils;

/// <summary>
/// Utility methods for AdminApi authorization checks.
/// These are used for endpoints that don't require X-Tenant-Id header (like tenant list operations).
/// </summary>
public static class AdminApiAuthUtils
{
    /// <summary>
    /// Checks if the current user is a system admin by querying the database directly.
    /// This is used for endpoints that don't have tenant context (no X-Tenant-Id header).
    /// </summary>
    /// <param name="tenantContext">The tenant context containing the logged-in user ID</param>
    /// <param name="userRepository">The user repository to query the database</param>
    /// <returns>
    /// Tuple of (isSysAdmin, errorResult).
    /// - If isSysAdmin is true, errorResult is null
    /// - If isSysAdmin is false, errorResult contains the appropriate HTTP result
    /// - If user not found or error, errorResult contains the error response
    /// </returns>
    public static async Task<(bool isSysAdmin, IResult? errorResult)> CheckSysAdminAsync(
        ITenantContext tenantContext,
        IUserRepository userRepository,
        HttpContext? httpContext = null,
        ILogger? logger = null)
    {
        var userId = tenantContext.LoggedInUser;
        logger?.LogInformation("AdminApiAuthUtils: Checking SysAdmin for userId from context: '{UserId}'", userId);
        
        if (string.IsNullOrEmpty(userId))
        {
            logger?.LogWarning("AdminApiAuthUtils: UserId is null or empty in tenant context");
            return (false, Results.Json(
                new { error = "Unauthorized", message = "User not found in context" },
                statusCode: 401));
        }

        // Try to find user by userId (could be preferred_username, UUID from token's 'sub' claim, or other)
        var user = await userRepository.GetByUserIdAsync(userId);
        logger?.LogInformation("AdminApiAuthUtils: User lookup by userId '{UserId}' - Found: {Found}, IsSysAdmin: {IsSysAdmin}", 
            userId, user != null, user?.IsSysAdmin ?? false);
        
        // If not found, try fallback lookups from token claims
        if (user == null && httpContext?.User != null)
        {
            logger?.LogWarning("AdminApiAuthUtils: User not found by userId '{UserId}'. Trying fallback lookups...", userId);
            
            // Fallback 1: Try preferred_username from token claims
            var preferredUsername = httpContext.User.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
            if (!string.IsNullOrEmpty(preferredUsername) && preferredUsername != userId)
            {
                logger?.LogInformation("AdminApiAuthUtils: Fallback 1 - Trying lookup by preferred_username: '{PreferredUsername}'", preferredUsername);
                user = await userRepository.GetByUserIdAsync(preferredUsername);
                if (user != null)
                {
                    logger?.LogInformation("AdminApiAuthUtils: Found user by preferred_username '{PreferredUsername}'", preferredUsername);
                }
            }
            
            // Fallback 2: If still not found, try email from token claims
            if (user == null)
            {
                var email = httpContext.User.Claims.FirstOrDefault(c => c.Type == "email" || c.Type == ClaimTypes.Email)?.Value;
                if (!string.IsNullOrEmpty(email))
                {
                    logger?.LogInformation("AdminApiAuthUtils: Fallback 2 - Trying lookup by email: '{Email}'", email);
                    user = await userRepository.GetByUserEmailAsync(email);
                    if (user != null)
                    {
                        logger?.LogInformation("AdminApiAuthUtils: Found user by email '{Email}'", email);
                    }
                }
            }
            
            // If still not found after all fallbacks, return helpful error
            if (user == null)
            {
                logger?.LogWarning("AdminApiAuthUtils: User not found after all fallback lookups. Token userId: '{UserId}'. Tried preferred_username and email.", userId);
                return (false, Results.Json(
                    new { 
                        error = "Unauthorized", 
                        message = $"User not found. Token userId: '{userId}'. Tried lookups by preferred_username and email. Please ensure user exists in database with matching user_id or email." 
                    },
                    statusCode: 401));
            }
        }
        
        if (user == null)
        {
            logger?.LogWarning("AdminApiAuthUtils: User not found by userId '{UserId}'", userId);
            return (false, Results.Json(
                new { error = "Unauthorized", message = "User not found" },
                statusCode: 401));
        }

        if (!user.IsSysAdmin)
        {
            logger?.LogWarning("AdminApiAuthUtils: User '{UserId}' (DB user_id: '{DbUserId}') is not a SysAdmin. IsSysAdmin: {IsSysAdmin}", 
                userId, user.UserId, user.IsSysAdmin);
            return (false, Results.Json(
                new { error = "Forbidden", message = "Access denied: System admin permissions required" },
                statusCode: 403));
        }

        logger?.LogInformation("AdminApiAuthUtils: User '{UserId}' (DB user_id: '{DbUserId}') is confirmed as SysAdmin", 
            userId, user.UserId);
        return (true, null);
    }

    /// <summary>
    /// Checks if the current user is a system admin or tenant admin by querying the database directly.
    /// This is used for endpoints that don't have tenant context (no X-Tenant-Id header).
    /// </summary>
    /// <param name="tenantContext">The tenant context containing the logged-in user ID</param>
    /// <param name="userRepository">The user repository to query the database</param>
    /// <returns>
    /// Tuple of (isAdmin, errorResult).
    /// - If isAdmin is true, errorResult is null
    /// - If isAdmin is false, errorResult contains the appropriate HTTP result
    /// - If user not found or error, errorResult contains the error response
    /// </returns>
    public static async Task<(bool isAdmin, IResult? errorResult)> CheckAdminAsync(
        ITenantContext tenantContext,
        IUserRepository userRepository)
    {
        var userId = tenantContext.LoggedInUser;
        if (string.IsNullOrEmpty(userId))
        {
            return (false, Results.Json(
                new { error = "Unauthorized", message = "User not found in context" },
                statusCode: 401));
        }

        var user = await userRepository.GetByUserIdAsync(userId);
        if (user == null)
        {
            return (false, Results.Json(
                new { error = "Unauthorized", message = "User not found" },
                statusCode: 401));
        }

        // SysAdmin has access to everything
        if (user.IsSysAdmin)
        {
            return (true, null);
        }

        // For tenant admin check, we'd need tenant context, but since we don't have X-Tenant-Id,
        // we can only check SysAdmin. Tenant admins would need to provide X-Tenant-Id header.
        // So for tenant list operations, only SysAdmin can access.
        return (false, Results.Json(
            new { error = "Forbidden", message = "Access denied: Admin permissions required" },
            statusCode: 403));
    }
}

