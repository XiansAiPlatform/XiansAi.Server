using Shared.Auth;
using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;
using Shared.Data.Models;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for tenant management.
/// These are administrative operations for managing tenants.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminTenantEndpoints
{
    /// <summary>
    /// Route name used to generate the logo URL that is embedded in tenant responses.
    /// </summary>
    private const string LogoRouteName = "AdminGetTenantLogo";

    /// <summary>
    /// Maps all AdminApi tenant endpoints.
    /// </summary>
    public static void MapAdminTenantEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminTenantGroup = adminApiGroup.MapGroup("/tenants")
            .WithTags("AdminAPI - Tenant Management")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // List All Tenants - SysAdmin only (prevents TenantAdmin from enumerating all tenants)
        adminTenantGroup.MapGet("", async (
            HttpContext httpContext,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: List tenants requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can list all tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            var result = await tenantService.GetAllTenants();
            if (result.IsSuccess && result.Data != null)
            {
                foreach (var tenant in result.Data)
                {
                    ReplaceLogoWithUrl(tenant, httpContext, linkGenerator);
                }
            }
            return result.ToHttpResult();
        })
        .WithName("ListTenants")
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Get Tenant by TenantId - SysAdmin only (TenantAdmin should use tenant-scoped endpoints)
        adminTenantGroup.MapGet("/{tenantId}", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Get tenant by ID requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can retrieve tenant details by ID" },
                    statusCode: StatusCodes.Status403Forbidden);
            }
            var result = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (result.IsSuccess)
            {
                ReplaceLogoWithUrl(result.Data, httpContext, linkGenerator);
            }
            return result.ToHttpResult();
        })
        .WithName("GetTenantByTenantId")
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Get Tenant Logo - serves the image so the (potentially large) base64 payload
        // does not have to be embedded in every tenant response.
        adminTenantGroup.MapGet("/{tenantId}/logo", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            var result = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!result.IsSuccess || result.Data == null)
            {
                return result.ToHttpResult();
            }

            var logo = result.Data.Logo;
            if (logo == null)
            {
                return Results.NotFound(new { message = "Tenant has no logo" });
            }

            // Logo stored as an external URL: redirect to the source image.
            if (!string.IsNullOrEmpty(logo.Url))
            {
                return Results.Redirect(logo.Url);
            }

            if (string.IsNullOrEmpty(logo.ImgBase64))
            {
                return Results.NotFound(new { message = "Tenant has no logo" });
            }

            byte[] imageBytes;
            try
            {
                imageBytes = Convert.FromBase64String(logo.ImgBase64);
            }
            catch (FormatException ex)
            {
                logger.LogError(ex, "Tenant {TenantId} has invalid base64 logo data", tenantId);
                return Results.Problem("Stored logo image is invalid", statusCode: StatusCodes.Status500InternalServerError);
            }

            var contentType = DetectImageContentType(imageBytes);
            httpContext.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(imageBytes, contentType);
        })
        .WithName(LogoRouteName)
        .Produces(StatusCodes.Status302Found)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Create Tenant - No X-Tenant-Id header required (creating new tenant)
        adminTenantGroup.MapPost("", async (
            [FromBody] CreateTenantRequest request,
            HttpContext httpContext,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Create tenant requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can create tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            var createdBy = tenantContext.LoggedInUser ?? "system";
            var result = await tenantService.CreateTenant(request, createdBy);
            if (result.IsSuccess && result.Data != null)
            {
                ReplaceLogoWithUrl(result.Data.Tenant, httpContext, linkGenerator);
            }
            return result.ToHttpResult();
        })
        .WithName("CreateTenant")
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Update Tenant - SysAdmin only
        adminTenantGroup.MapPatch("/{tenantId}", async (
            string tenantId,
            [FromBody] UpdateTenantRequest request,
            HttpContext httpContext,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Update tenant requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can update tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // First get tenant by tenantId to get the ObjectId
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }
            
            // Use the ObjectId for the update operation
            var result = await tenantService.UpdateTenant(tenantResult.Data.Id, request);
            if (result.IsSuccess)
            {
                ReplaceLogoWithUrl(result.Data, httpContext, linkGenerator);
            }
            return result.ToHttpResult();
        })
        .WithName("UpdateTenant")
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Delete Tenant - SysAdmin only
        adminTenantGroup.MapDelete("/{tenantId}", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService,
            [FromServices] ILogger<ITenantService> logger) =>
        {
            if (tenantContext.UserRoles?.Contains(SystemRoles.SysAdmin) != true)
            {
                logger.LogWarning("Access denied: Delete tenant requires SysAdmin role. User: {UserId}", tenantContext.LoggedInUser);
                return Results.Json(
                    new { message = "Access denied: Only system administrators can delete tenants" },
                    statusCode: StatusCodes.Status403Forbidden);
            }

            // First get tenant by tenantId to get the ObjectId
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }
            
            // Use the ObjectId for the delete operation
            var result = await tenantService.DeleteTenant(tenantResult.Data.Id);
            return result.ToHttpResult();
        })
        .WithName("DeleteTenant")
        .Produces(StatusCodes.Status403Forbidden)
        ;
    }

    /// <summary>
    /// Replaces an embedded base64 logo with a URL pointing to the dedicated logo endpoint,
    /// so tenant responses never carry the (potentially large) base64 payload.
    /// Logos stored as external URLs are left untouched.
    /// </summary>
    private static void ReplaceLogoWithUrl(Tenant? tenant, HttpContext httpContext, LinkGenerator linkGenerator)
    {
        var logo = tenant?.Logo;
        if (logo == null || string.IsNullOrEmpty(logo.ImgBase64))
        {
            return;
        }

        var logoUrl = linkGenerator.GetUriByName(
            httpContext,
            LogoRouteName,
            new { tenantId = tenant!.TenantId });

        if (!string.IsNullOrEmpty(logoUrl))
        {
            logo.Url = logoUrl;
        }

        // Never expose the base64 payload in tenant responses, regardless of URL generation.
        logo.ImgBase64 = null;
    }

    /// <summary>
    /// Best-effort detection of an image content type from its leading magic bytes.
    /// Stored base64 logos do not carry a MIME type, so we sniff the decoded bytes.
    /// </summary>
    private static string DetectImageContentType(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return "image/png";
        }
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }
        if (bytes.Length >= 3 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
        {
            return "image/gif";
        }
        if (bytes.Length >= 12 &&
            bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
        {
            return "image/webp";
        }
        if (bytes.Length >= 2 && bytes[0] == 0x42 && bytes[1] == 0x4D)
        {
            return "image/bmp";
        }
        // SVG is text based and starts with '<' (e.g. "<svg" or "<?xml").
        if (bytes.Length >= 1 && bytes[0] == (byte)'<')
        {
            return "image/svg+xml";
        }
        return "application/octet-stream";
    }
}
