namespace Features.AdminApi.Constants;

/// <summary>
/// Constants for AdminApi configuration, including API versioning.
/// </summary>
public static class AdminApiConstants
{
    /// <summary>
    /// Current API version. Change this when introducing a new version.
    /// </summary>
    public const string CurrentVersion = "v1";

    /// <summary>
    /// Base path for AdminApi endpoints (without version).
    /// </summary>
    public const string BasePath = "/api/{version}/admin";

    /// <summary>
    /// Gets the versioned base path for AdminApi endpoints.
    /// </summary>
    /// <param name="version">API version (defaults to CurrentVersion)</param>
    /// <returns>Versioned base path (e.g., "/api/v1/admin")</returns>
    public static string GetVersionedBasePath(string? version = null)
    {
        return BasePath.Replace("{version}", version ?? CurrentVersion);
    }

    /// <summary>
    /// Gets the versioned path for a specific AdminApi resource.
    /// </summary>
    /// <param name="resource">Resource path (e.g., "tenants", "agents")</param>
    /// <param name="version">API version (defaults to CurrentVersion)</param>
    /// <returns>Versioned resource path (e.g., "/api/v1/admin/tenants")</returns>
    public static string GetVersionedPath(string resource, string? version = null)
    {
        var basePath = GetVersionedBasePath(version);
        return $"{basePath}/{resource.TrimStart('/')}";
    }

    /// <summary>
    /// Builds a full versioned path for a resource with parameters.
    /// Useful for Results.Created() location headers.
    /// </summary>
    /// <param name="resourcePath">Full resource path with parameters (e.g., "tenants/{tenantId}/agents/{agentId}")</param>
    /// <param name="version">API version (defaults to CurrentVersion)</param>
    /// <returns>Full versioned path (e.g., "/api/v1/admin/tenants/{tenantId}/agents/{agentId}")</returns>
    public static string BuildVersionedPath(string resourcePath, string? version = null)
    {
        var basePath = GetVersionedBasePath(version);
        return $"{basePath}/{resourcePath.TrimStart('/')}";
    }
}


