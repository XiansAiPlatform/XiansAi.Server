using Features.WebApi.Auth.Providers.Auth0;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Features.WebApi.Auth.Providers;

/// <summary>
/// Interface for authentication providers
/// </summary>
public interface IAuthProvider
{
    /// <summary>
    /// Configure the JWT bearer options for this provider
    /// </summary>
    void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration);
    
    /// <summary>
    /// Validate a token and extract claims
    /// </summary>
    Task<(bool success, string? userId, IEnumerable<string>? tenantIds)> ValidateToken(string token);
    
    /// <summary>
    /// Get user information from the provider
    /// </summary>
    Task<UserInfo> GetUserInfo(string userId);
    
    /// <summary>
    /// Set a new tenant for the user
    /// </summary>
    Task<string> SetNewTenant(string userId, string tenantId);
}

/// <summary>
/// Provider type enum
/// </summary>
public enum AuthProviderType
{
    Auth0,
    AzureB2C
}

/// <summary>
/// Base configuration for auth providers
/// </summary>
public class AuthProviderConfig
{
    public AuthProviderType Provider { get; set; } = AuthProviderType.Auth0;
    public string TenantClaimType { get; set; } = "https://xians.ai/tenants";
} 