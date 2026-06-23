using Shared.Data.Models;

namespace Features.AdminApi.Utils;

/// <summary>
/// Shared logic for converting a tenant's base64-stored logo into a URL pointing to the
/// dedicated tenant logo endpoint, so the (potentially large) base64 payload is never embedded
/// in admin API responses. Logos stored as external URLs are left untouched.
/// </summary>
public static class TenantLogoHelper
{
    /// <summary>
    /// Route name of the endpoint that serves a tenant's logo image.
    /// Must match the route name registered in <c>AdminTenantEndpoints</c>.
    /// </summary>
    public const string LogoRouteName = "AdminGetTenantLogo";

    /// <summary>
    /// Mutates the supplied tenant in place: a base64-stored logo is replaced with a URL and its
    /// base64 payload is cleared. Use this when the tenant instance itself is the response payload.
    /// Callers should pass a copy if the tenant is shared/cached to avoid mutating the original.
    /// </summary>
    public static void ApplyLogoUrl(Tenant? tenant, HttpContext httpContext, LinkGenerator linkGenerator)
    {
        var logo = tenant?.Logo;
        if (logo == null || string.IsNullOrEmpty(logo.ImgBase64))
        {
            return;
        }

        var logoUrl = GenerateLogoUrl(tenant!.TenantId, httpContext, linkGenerator);
        if (!string.IsNullOrEmpty(logoUrl))
        {
            logo.Url = logoUrl;
        }

        // Never expose the base64 payload, regardless of URL generation.
        logo.ImgBase64 = null;
    }

    /// <summary>
    /// Builds a logo for a response without mutating the source tenant. A base64-stored logo is
    /// converted to a URL-only logo; an external URL logo is returned as-is; null stays null.
    /// </summary>
    public static Logo? BuildLogoResponse(Tenant tenant, HttpContext httpContext, LinkGenerator linkGenerator)
    {
        var logo = tenant.Logo;
        if (logo == null || string.IsNullOrEmpty(logo.ImgBase64))
        {
            return logo;
        }

        var logoUrl = GenerateLogoUrl(tenant.TenantId, httpContext, linkGenerator);

        return new Logo
        {
            Url = string.IsNullOrEmpty(logoUrl) ? null : logoUrl,
            ImgBase64 = null,
            Width = logo.Width,
            Height = logo.Height
        };
    }

    private static string? GenerateLogoUrl(string tenantId, HttpContext httpContext, LinkGenerator linkGenerator)
    {
        return linkGenerator.GetUriByName(
            httpContext,
            LogoRouteName,
            new { tenantId });
    }
}
