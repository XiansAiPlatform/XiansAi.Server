using Shared.Data.Models;

namespace Features.AdminApi.Utils;

/// <summary>
/// Replaces a tenant's base64 logo and favicon with links to the endpoints that serve them. This
/// keeps large base64 images out of admin API responses. Images stored as outside URLs are not
/// changed.
/// </summary>
public static class TenantLogoHelper
{
    /// <summary>
    /// Route name of the endpoint that serves a tenant's logo image.
    /// Must match the route name registered in <c>AdminTenantEndpoints</c>.
    /// </summary>
    public const string LogoRouteName = "AdminGetTenantLogo";

    /// <summary>
    /// Name of the endpoint that serves a tenant's favicon.
    /// Must be the same as the name used in <c>AdminTenantEndpoints</c>.
    /// </summary>
    public const string FaviconRouteName = "AdminGetTenantFavicon";

    /// <summary>
    /// Changes the given tenant directly: a base64 logo or favicon is replaced with a link, and the
    /// base64 data is removed. Use this when the tenant itself is sent back in the response. If the
    /// tenant comes from a cache or is shared, pass a copy so the original is not changed.
    /// </summary>
    public static void ApplyImageUrls(Tenant? tenant, HttpContext httpContext, LinkGenerator linkGenerator)
    {
        if (tenant == null)
        {
            return;
        }

        ApplyImageUrl(tenant.Logo, tenant.TenantId, LogoRouteName, httpContext, linkGenerator);
        ApplyImageUrl(tenant.Favicon, tenant.TenantId, FaviconRouteName, httpContext, linkGenerator);
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

        var logoUrl = GenerateUrl(LogoRouteName, tenant.TenantId, httpContext, linkGenerator);

        return new Logo
        {
            Url = string.IsNullOrEmpty(logoUrl) ? null : logoUrl,
            ImgBase64 = null,
            Width = logo.Width,
            Height = logo.Height
        };
    }

    private static void ApplyImageUrl(
        Logo? image,
        string tenantId,
        string routeName,
        HttpContext httpContext,
        LinkGenerator linkGenerator)
    {
        if (image == null || string.IsNullOrEmpty(image.ImgBase64))
        {
            return;
        }

        var url = GenerateUrl(routeName, tenantId, httpContext, linkGenerator);
        if (!string.IsNullOrEmpty(url))
        {
            image.Url = url;
        }

        // Never send the base64 data, even if the link could not be built.
        image.ImgBase64 = null;
    }

    private static string? GenerateUrl(string routeName, string tenantId, HttpContext httpContext, LinkGenerator linkGenerator)
    {
        return linkGenerator.GetUriByName(
            httpContext,
            routeName,
            new { tenantId });
    }
}
