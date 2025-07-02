namespace Shared.Configuration;

/// <summary>
/// Configuration settings for default data seeding during application startup.
/// These settings can be configured in appsettings.json to control what default data is created.
/// </summary>
public class SeedDataSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "SeedData";
    
    /// <summary>
    /// Whether to enable data seeding during startup. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>
    /// Whether to create a default tenant if no tenants exist. Default is true.
    /// </summary>
    public bool CreateDefaultTenant { get; set; } = true;
    
    /// <summary>
    /// Configuration for the default tenant
    /// </summary>
    public DefaultTenantSettings DefaultTenant { get; set; } = new();
}

/// <summary>
/// Configuration for the default tenant that gets created during seeding
/// </summary>
public class DefaultTenantSettings
{
    /// <summary>
    /// The tenant ID for the default tenant. Default is "default".
    /// </summary>
    public string TenantId { get; set; } = "default";
    
    /// <summary>
    /// The name for the default tenant. Default is "Default Tenant".
    /// </summary>
    public string Name { get; set; } = "Default Tenant";
    
    /// <summary>
    /// The domain for the default tenant. Default is "default.xiansai.com".
    /// </summary>
    public string Domain { get; set; } = "default.xiansai.com";
    
    /// <summary>
    /// Whether the default tenant should be enabled. Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
} 