using System.Text.Json.Nodes;
using Shared.Auth;
using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Utils;
using Shared.Utils.Services;
using Shared.Data.Models;
using Features.AdminApi.Auth;
using Features.AdminApi.Utils;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi endpoints for tenant management.
/// These are administrative operations for managing tenants.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminTenantEndpoints
{

    /// <summary>
    /// Maps all AdminApi tenant endpoints.
    /// </summary>
    public static void MapAdminTenantEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminTenantGroup = adminApiGroup.MapGroup("/tenants")
            .WithTags("AdminAPI - Tenant Management")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .EnforceCapabilities();

        // List All Tenants - SysAdmin only by default (prevents TenantAdmin from enumerating all
        // tenants), enforced by the capability matrix (tenants.list); see CapabilityActions.
        // Supports pagination via optional "page" (default 1) and "pageSize" (default 20, max 100)
        // query params, plus an optional "search" term matched case-insensitively against
        // tenantId, name, domain and description.
        adminTenantGroup.MapGet("", async (
            HttpContext httpContext,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            [FromQuery] string? search,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] ITenantService tenantService) =>
        {
            var result = await tenantService.GetAllTenants(page, pageSize, search);
            if (result.IsSuccess && result.Data != null)
            {
                foreach (var tenant in result.Data.Tenants)
                {
                    TenantLogoHelper.ApplyLogoUrl(tenant, httpContext, linkGenerator);
                }
            }
            return result.ToHttpResult();
        })
        .WithName("ListTenants")
        .RequireCapability(CapabilityActions.TenantsList)
        .Produces(StatusCodes.Status403Forbidden)
        .WithMetadata(TenantOptionalForSysAdminMetadata.Instance)
        ;

        // Get Tenant by TenantId - SysAdmin only by default (TenantAdmin should use tenant-scoped
        // endpoints), enforced by the capability matrix (tenants.get); see CapabilityActions.
        adminTenantGroup.MapGet("/{tenantId}", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] ITenantService tenantService) =>
        {
            // Honor the standard "Cache-Control: no-cache" request header: when present, read the
            // tenant directly from the database (bypassing, then refreshing, the tenant cache).
            var bypassCache = httpContext.Request.IsNoCacheRequested();
            var result = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted, bypassCache);
            if (result.IsSuccess)
            {
                TenantLogoHelper.ApplyLogoUrl(result.Data, httpContext, linkGenerator);
            }
            return result.ToHttpResult();
        })
        .WithName("GetTenantByTenantId")
        .RequireCapability(CapabilityActions.TenantsGet)
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Get Tenant Metadata - SysAdmin only by default, enforced by the capability matrix
        // (tenants.metadata.list); see CapabilityActions. This is the only endpoint that returns
        // metadata with Secret values decrypted; tenant payloads elsewhere carry the
        // stored (encrypted) form.
        adminTenantGroup.MapGet("/{tenantId}/metadata", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService) =>
        {
            var bypassCache = httpContext.Request.IsNoCacheRequested();
            var result = await tenantService.GetTenantMetadata(tenantId, httpContext.RequestAborted, bypassCache);
            return result.ToHttpResult();
        })
        .WithName("GetTenantMetadata")
        .RequireCapability(CapabilityActions.TenantsMetadataList)
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Get a single Tenant Metadata entry by key (case-insensitive) - SysAdmin only by default,
        // enforced by the capability matrix (tenants.metadata.get); see CapabilityActions.
        // Returns the entry with its value decrypted when the type is Secret.
        adminTenantGroup.MapGet("/{tenantId}/metadata/{key}", async (
            string tenantId,
            string key,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService) =>
        {
            var bypassCache = httpContext.Request.IsNoCacheRequested();
            var result = await tenantService.GetTenantMetadataByKey(tenantId, key, httpContext.RequestAborted, bypassCache);
            return result.ToHttpResult();
        })
        .WithName("GetTenantMetadataByKey")
        .RequireCapability(CapabilityActions.TenantsMetadataGet)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Upsert a single Tenant Metadata entry by key (case-insensitive) - SysAdmin only by
        // default, enforced by the capability matrix (tenants.metadata.upsert); see
        // CapabilityActions. Adds the entry when the key does not exist, otherwise replaces its
        // value/type. Secret values are encrypted before persisting.
        adminTenantGroup.MapPut("/{tenantId}/metadata/{key}", async (
            string tenantId,
            string key,
            [FromBody] UpsertTenantMetadataRequest request,
            [FromServices] ITenantService tenantService) =>
        {
            var result = await tenantService.UpsertTenantMetadata(tenantId, key, request);
            return result.ToHttpResult();
        })
        .WithName("UpsertTenantMetadata")
        .RequireCapability(CapabilityActions.TenantsMetadataUpsert)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Delete a single Tenant Metadata entry by key (case-insensitive) - SysAdmin only by
        // default, enforced by the capability matrix (tenants.metadata.delete); see
        // CapabilityActions.
        adminTenantGroup.MapDelete("/{tenantId}/metadata/{key}", async (
            string tenantId,
            string key,
            [FromServices] ITenantService tenantService) =>
        {
            var result = await tenantService.DeleteTenantMetadata(tenantId, key);
            return result.ToHttpResult();
        })
        .WithName("DeleteTenantMetadata")
        .RequireCapability(CapabilityActions.TenantsMetadataDelete)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound)
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
                logger.LogError(ex, "Tenant {TenantId} has invalid base64 logo data", LogSanitizer.Sanitize(tenantId));
                return Results.Problem("Stored logo image is invalid", statusCode: StatusCodes.Status500InternalServerError);
            }

            var contentType = DetectImageContentType(imageBytes);
            httpContext.Response.Headers.CacheControl = "private, max-age=3600";
            return Results.File(imageBytes, contentType);
        })
        .WithName(TenantLogoHelper.LogoRouteName)
        .RequireCapability(CapabilityActions.TenantLogoGet)
        .Produces(StatusCodes.Status302Found)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Get Tenant Theme - returns the tenant's theme identifier. Access is scoped by the
        // service (SysAdmin for any tenant, TenantAdmin for their own), mirroring the logo GET.
        adminTenantGroup.MapGet("/{tenantId}/theme", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService) =>
        {
            var result = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!result.IsSuccess || result.Data == null)
            {
                return result.ToHttpResult();
            }

            return Results.Ok(new { theme = result.Data.Theme });
        })
        .WithName("GetTenantTheme")
        .RequireCapability(CapabilityActions.TenantThemeGet)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Set Tenant Theme - creates or replaces the theme.
        adminTenantGroup.MapPut("/{tenantId}/theme", async (
            string tenantId,
            [FromBody] UpdateTenantThemeRequest request,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService) =>
        {
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }

            var result = await tenantService.UpdateTenantTheme(tenantResult.Data.Id, request.Theme);
            return result.ToHttpResult();
        })
        .WithName("SetTenantTheme")
        .RequireCapability(CapabilityActions.TenantThemeSet)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Clear Tenant Theme - removes the theme.
        adminTenantGroup.MapDelete("/{tenantId}/theme", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService) =>
        {
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }

            var result = await tenantService.UpdateTenantTheme(tenantResult.Data.Id, null);
            return result.ToHttpResult();
        })
        .WithName("ClearTenantTheme")
        .RequireCapability(CapabilityActions.TenantThemeClear)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Set Tenant Logo - creates or replaces the logo. Accepts either an external URL or a
        // base64-encoded image (validated by the Logo model). The response logo is rewritten to a
        // URL so the (potentially large) base64 payload is never echoed back.
        adminTenantGroup.MapPut("/{tenantId}/logo", async (
            string tenantId,
            [FromBody] Logo request,
            HttpContext httpContext,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] ITenantService tenantService) =>
        {
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }

            var result = await tenantService.UpdateTenantLogo(tenantResult.Data.Id, request);
            if (result.IsSuccess)
            {
                TenantLogoHelper.ApplyLogoUrl(result.Data, httpContext, linkGenerator);
            }
            return result.ToHttpResult();
        })
        .WithName("SetTenantLogo")
        .RequireCapability(CapabilityActions.TenantLogoSet)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Clear Tenant Logo - removes the logo.
        adminTenantGroup.MapDelete("/{tenantId}/logo", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService) =>
        {
            var tenantResult = await tenantService.GetTenantByTenantId(tenantId, httpContext.RequestAborted);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                return tenantResult.ToHttpResult();
            }

            var result = await tenantService.UpdateTenantLogo(tenantResult.Data.Id, null);
            return result.ToHttpResult();
        })
        .WithName("ClearTenantLogo")
        .RequireCapability(CapabilityActions.TenantLogoClear)
        .Produces(StatusCodes.Status404NotFound)
        ;

        // Create Tenant - No X-Tenant-Id header required (creating new tenant). SysAdmin only by
        // default, enforced by the capability matrix (tenants.create); see CapabilityActions.
        adminTenantGroup.MapPost("", async (
            [FromBody] CreateTenantRequest request,
            HttpContext httpContext,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ITenantService tenantService) =>
        {
            var createdBy = tenantContext.LoggedInUser ?? "system";
            var result = await tenantService.CreateTenant(request, createdBy);
            if (result.IsSuccess && result.Data != null)
            {
                TenantLogoHelper.ApplyLogoUrl(result.Data.Tenant, httpContext, linkGenerator);
            }
            return result.ToHttpResult();
        })
        .WithName("CreateTenant")
        .RequireCapability(CapabilityActions.TenantsCreate)
        .Produces(StatusCodes.Status403Forbidden)
        .WithMetadata(TenantOptionalForSysAdminMetadata.Instance)
        ;

        // Update Tenant - SysAdmin only by default, enforced by the capability matrix
        // (tenants.update); see CapabilityActions.
        adminTenantGroup.MapPatch("/{tenantId}", async (
            string tenantId,
            [FromBody] UpdateTenantRequest request,
            HttpContext httpContext,
            [FromServices] LinkGenerator linkGenerator,
            [FromServices] ITenantService tenantService) =>
        {
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
                TenantLogoHelper.ApplyLogoUrl(result.Data, httpContext, linkGenerator);
            }
            return result.ToHttpResult();
        })
        .WithName("UpdateTenant")
        .RequireCapability(CapabilityActions.TenantsUpdate)
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Delete Tenant - SysAdmin only by default, enforced by the capability matrix
        // (tenants.delete); see CapabilityActions.
        adminTenantGroup.MapDelete("/{tenantId}", async (
            string tenantId,
            HttpContext httpContext,
            [FromServices] ITenantService tenantService) =>
        {
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
        .RequireCapability(CapabilityActions.TenantsDelete)
        .Produces(StatusCodes.Status403Forbidden)
        ;

        // Per-tenant OIDC token-acceptance configuration — SysAdmin only.
        MapTenantOidcConfigEndpoints(adminApiGroup);

        // Per-tenant Temporal connection override — SysAdmin only.
        MapTenantTemporalConfigEndpoints(adminApiGroup);
    }

    /// <summary>
    /// Maps the per-tenant Temporal connection override endpoints
    /// (<c>/tenants/{tenantId}/temporal-config</c>). Restricted to SysAdmin: the override carries
    /// TLS client credentials and determines which Temporal server the tenant's workflows run on.
    /// </summary>
    private static void MapTenantTemporalConfigEndpoints(RouteGroupBuilder adminApiGroup)
    {
        var temporalGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/temporal-config")
            .WithTags("AdminAPI - Tenant Temporal Config")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>()
            .EnforceCapabilities();

        // Get the tenant's Temporal override. Returns the config directly, or null when none exists.
        temporalGroup.MapGet("", async (
            string tenantId,
            [FromServices] ITenantTemporalConfigService service) =>
        {
            var result = await service.GetForTenantAsync(tenantId);
            return result.ToHttpResult();
        })
        .WithName("AdminGetTenantTemporalConfig")
        .RequireCapability(CapabilityActions.TenantTemporalConfigGet)
        .WithSummary("Get the tenant Temporal connection override")
        .WithDescription("Returns the tenant's dedicated Temporal connection, or null when none is configured.");

        // Create or replace the tenant's Temporal override.
        temporalGroup.MapPut("", UpsertTenantTemporalConfig)
            .WithName("AdminUpdateTenantTemporalConfig")
        .RequireCapability(CapabilityActions.TenantTemporalConfigSet)
            .WithSummary("Set the tenant Temporal connection override")
            .WithDescription("Creates or replaces the tenant's dedicated Temporal server connection. The tenantId is taken from the route.");

        temporalGroup.MapPost("", UpsertTenantTemporalConfig)
            .WithName("AdminCreateTenantTemporalConfig")
        .RequireCapability(CapabilityActions.TenantTemporalConfigSet)
            .WithSummary("Set the tenant Temporal connection override")
            .WithDescription("Creates or replaces the tenant's dedicated Temporal server connection. The tenantId is taken from the route.");


        temporalGroup.MapPost("/revert", RevertTenantTemporalConfig)
        .WithName("AdminRevertTenantTemporalConfig")
        .RequireCapability(CapabilityActions.TenantTemporalConfigRevert)
        .WithSummary("Revert the tenant Temporal connection")
        .WithDescription("Reverts the tenant to the platform's default Temporal server.");

        temporalGroup.MapPost("/test-connection", TestTenantTemporalConnection)
        .WithName("AdminTestTenantTemporalConnection")
        .RequireCapability(CapabilityActions.TenantTemporalConfigTestConnection)
        .WithSummary("Test a Temporal connection")
        .WithDescription("Attempts to connect with the given server URL/namespace/credentials without saving anything.");
    }

    private static async Task<IResult> UpsertTenantTemporalConfig(
        string tenantId,
        [FromBody] UpsertTenantTemporalConfigRequest request,
        [FromServices] ITenantTemporalConfigService service,
        [FromServices] ITenantContext tenantContext)
    {
        var actor = tenantContext.LoggedInUser ?? "system";
        var result = await service.UpsertAsync(
            tenantId, request.ServerUrl, request.Namespace, request.Certificate, request.PrivateKey, actor);
        return result.ToHttpResult();
    }

    private static async Task<IResult> RevertTenantTemporalConfig(
        string tenantId,
        [FromBody] UpsertTenantTemporalConfigRequest request,
        [FromServices] ITenantTemporalConfigService service,
        [FromServices] ITenantContext tenantContext)
    {
            var actor = tenantContext.LoggedInUser ?? "system";
            var result = await service.RevertAsync(tenantId, actor);
            return result.ToHttpResult();
    }

    private static async Task<IResult> TestTenantTemporalConnection(
        string tenantId,
        [FromBody] UpsertTenantTemporalConfigRequest request,
        [FromServices] ITenantTemporalConfigService service)
    {
        var result = await service.CheckConnectivityAsync(
            tenantId, request.ServerUrl, request.Namespace, request.Certificate, request.PrivateKey);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Maps the per-tenant OIDC configuration management endpoints, mirroring the WebApi
    /// <c>OidcConfigEndpoints</c> but tenant-scoped via the route (<c>/tenants/{tenantId}/oidc-config</c>).
    /// Restricted to SysAdmin: OIDC provider acceptance rules are a platform-level security control.
    /// The <see cref="TenantRouteScopeFilter"/> still guarantees the route tenant matches the
    /// authenticated caller's resolved tenant.
    /// </summary>
    private static void MapTenantOidcConfigEndpoints(RouteGroupBuilder adminApiGroup)
    {
        var oidcGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/oidc-config")
            .WithTags("AdminAPI - Tenant OIDC Config")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>()
            .EnforceCapabilities();

        // Get the tenant's OIDC configuration (null when none is configured).
        oidcGroup.MapGet("", GetOidcConfig)
        .WithName("AdminGetTenantOidcConfig")
        .RequireCapability(CapabilityActions.TenantOidcConfigGet)
        .WithSummary("Get the tenant OIDC configuration")
        .WithDescription("Returns the tenant-scoped OIDC token-acceptance configuration, or null when none exists.");

        // Create or replace the tenant's OIDC configuration.
        oidcGroup.MapPost("", UpsertOidcConfigCore)
            .WithName("AdminCreateTenantOidcConfig")
        .RequireCapability(CapabilityActions.TenantOidcConfigUpsert)
            .WithSummary("Create the tenant OIDC configuration")
            .WithDescription("Creates or replaces the tenant-scoped OIDC configuration. The tenantId is taken from the route.");

        oidcGroup.MapPut("", UpsertOidcConfigCore)
            .WithName("AdminUpdateTenantOidcConfig")
        .RequireCapability(CapabilityActions.TenantOidcConfigUpsert)
            .WithSummary("Update the tenant OIDC configuration")
            .WithDescription("Creates or replaces the tenant-scoped OIDC configuration. The tenantId is taken from the route.");

        // Remove the tenant's OIDC configuration.
        oidcGroup.MapDelete("", DeleteOidcConfig)
        .WithName("AdminDeleteTenantOidcConfig")
        .RequireCapability(CapabilityActions.TenantOidcConfigDelete)
        .WithSummary("Delete the tenant OIDC configuration")
        .WithDescription("Removes the tenant-scoped OIDC configuration.");

        // Return an example configuration pre-filled with the tenant id. Centralizes the template
        // so clients (e.g. the management UI) do not have to hard-code the schema themselves.
        oidcGroup.MapGet("/template", (string tenantId) =>
            Results.Ok(BuildOidcConfigTemplate(tenantId)))
        .WithName("AdminGetTenantOidcConfigTemplate")
        .RequireCapability(CapabilityActions.TenantOidcConfigTemplate)
        .WithSummary("Get an OIDC configuration template")
        .WithDescription("Returns a sample OIDC configuration (with the tenantId filled in) to use as a starting point.");
    }

    /// <summary>
    /// Reads a tenant's OIDC configuration. Internal (not private) so
    /// <see cref="AdminConsoleOidcEndpoints"/> can reuse it for the "admin-console" pseudo-tenant
    /// instead of duplicating the body.
    /// </summary>
    internal static async Task<IResult> GetOidcConfig(
        string tenantId,
        [FromServices] ITenantOidcConfigService service)
    {
        var result = await service.GetForTenantAsync(tenantId);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Removes a tenant's OIDC configuration. Internal for the same reason as <see cref="GetOidcConfig"/>.
    /// </summary>
    internal static async Task<IResult> DeleteOidcConfig(
        string tenantId,
        [FromServices] ITenantOidcConfigService service)
    {
        var result = await service.DeleteAsync(tenantId);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Shared handler for POST/PUT: validates the body, forces the tenant id to the given value
    /// (so callers cannot point a config at another tenant), and upserts via the service. Internal
    /// for the same reason as <see cref="GetOidcConfig"/> — <see cref="AdminConsoleOidcEndpoints"/>
    /// calls this directly with the "admin-console" pseudo-tenant instead of duplicating the body.
    /// </summary>
    internal static async Task<IResult> UpsertOidcConfigCore(
        string tenantId,
        [FromBody] JsonObject? config,
        [FromServices] ITenantOidcConfigService service,
        [FromServices] ITenantContext tenantContext)
    {
        if (config == null)
        {
            return Results.BadRequest(new { message = "A JSON configuration body is required" });
        }

        // The route tenant is authoritative (enforced by TenantRouteScopeFilter); make the payload match
        // so the service's tenantId consistency check always passes regardless of what the client sent.
        config["tenantId"] = tenantId;

        var actor = tenantContext.LoggedInUser ?? "system";
        var result = await service.UpsertAsync(tenantId, config.ToJsonString(), actor);
        return result.ToHttpResult();
    }

    /// <summary>
    /// Builds a sample <see cref="TenantOidcRules"/> for the given tenant. Kept in sync with the
    /// schema enforced by <see cref="TenantOidcConfigService"/> so it can be used directly as a starting point.
    /// Internal (not private) so <see cref="AdminConsoleOidcEndpoints"/> can reuse it for the
    /// "admin-console" pseudo-tenant instead of duplicating the template.
    /// </summary>
    internal static TenantOidcRules BuildOidcConfigTemplate(string tenantId) => new()
    {
        TenantId = tenantId,
        AllowedProviders = new List<string> { "google", "microsoft" },
        Providers = new Dictionary<string, OidcProviderRule>
        {
            ["google"] = new OidcProviderRule
            {
                Authority = "https://accounts.google.com",
                Issuer = "https://accounts.google.com",
                ExpectedAudience = new List<string> { "your-google-client-id.apps.googleusercontent.com" },
                Scope = "openid profile email",
                RequireSignedTokens = true,
                AcceptedAlgorithms = new List<string> { "RS256" },
                RequireHttpsMetadata = true,
                AdditionalClaims = new List<CustomClaimCheck>
                {
                    new() { Claim = "hd", Op = "equals", Value = "company.com" }
                },
                ProviderSpecificSettings = new Dictionary<string, object> { ["useHostedDomainCheck"] = true }
            },
            ["microsoft"] = new OidcProviderRule
            {
                Authority = "https://login.microsoftonline.com/common/v2.0",
                Issuer = "https://login.microsoftonline.com/{tenant}/v2.0",
                ExpectedAudience = new List<string> { "api://my-api", "account" },
                Scope = "openid profile email",
                RequireSignedTokens = true,
                AcceptedAlgorithms = new List<string> { "RS256", "RS384" },
                RequireHttpsMetadata = true,
                AdditionalClaims = new List<CustomClaimCheck>(),
                ProviderSpecificSettings = new Dictionary<string, object> { ["preferredTokenType"] = "id_token" }
            }
        },
        Notes = "Accept only google & microsoft issued tokens with detailed per-provider config."
    };

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
