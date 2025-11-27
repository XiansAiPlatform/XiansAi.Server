using MongoDB.Driver;
using MongoDB.Bson;
using Shared.Data;
using Shared.Data.Models;
using Shared.Configuration;
using Shared.Repositories;
using Shared.Data.Models.Usage;

namespace Features.WebApi.Scripts;

public class SeedData
{
    /// <summary>
    /// Seeds default data for the application. This method is designed to be called during startup
    /// and will only insert data if it doesn't already exist, minimizing performance overhead.
    /// </summary>
    /// <param name="serviceProvider">Service provider to resolve dependencies</param>
    /// <param name="logger">Logger for tracking seeding operations</param>
    public static async Task SeedDefaultDataAsync(IServiceProvider serviceProvider, ILogger logger)
    {
        try
        {
            using var scope = serviceProvider.CreateScope();
            var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
            var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
            var tenantRepository = scope.ServiceProvider.GetRequiredService<ITenantRepository>();
            var tokenUsageLimitRepository = scope.ServiceProvider.GetRequiredService<ITokenUsageLimitRepository>();
            
            // Get seeding configuration
            var seedSettings = configuration.GetSection(SeedDataSettings.SectionName).Get<SeedDataSettings>() ?? new SeedDataSettings();
            
            if (!seedSettings.Enabled)
            {
                logger.LogInformation("Data seeding is disabled via configuration");
                return;
            }
            
            logger.LogInformation("Starting default data seeding...");
            
            // Seed default tenant
            if (seedSettings.CreateDefaultTenant)
            {
                var created = await SeedDefaultTenantAsync(tenantRepository, seedSettings.DefaultTenant, logger);

                if (created && seedSettings.DefaultTenant.TokenUsage.Enabled)
                {
                    await SeedDefaultTenantTokenUsageAsync(tokenUsageLimitRepository, seedSettings.DefaultTenant, logger);
                }
            }
            
            // Add more seeding methods here as needed
            // await SeedDefaultAgentsAsync(agentRepository, logger);
            // await SeedDefaultRolesAsync(roleRepository, logger);
            
            logger.LogInformation("Default data seeding completed successfully");
        }
        catch (Exception ex)
        {
            // Log the error but don't throw - we want the application to continue even if seeding fails
            logger.LogWarning(ex, "Default data seeding failed, but application will continue");
        }
    }
    
    /// <summary>
    /// Seeds a default tenant if no tenants exist in the system.
    /// This ensures there's always at least one tenant available for the system to function.
    /// </summary>
    private static async Task<bool> SeedDefaultTenantAsync(ITenantRepository tenantRepository, DefaultTenantSettings tenantSettings, ILogger logger)
    {
        try
        {
            // Check if any tenants already exist (performance optimization)
            var existingTenants = await tenantRepository.GetAllAsync();
            if (existingTenants.Any())
            {
                logger.LogDebug("Tenants already exist, skipping default tenant seeding");
                return false;
            }
            
            logger.LogInformation("No tenants found, creating default tenant...");
            
            // Create default tenant using configuration settings
            var defaultTenant = new Tenant
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = tenantSettings.TenantId,
                Name = tenantSettings.Name,
                Domain = tenantSettings.Domain,
                Enabled = tenantSettings.Enabled,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            
            await tenantRepository.CreateAsync(defaultTenant);
            logger.LogInformation("Default tenant created successfully with ID: {TenantId}", defaultTenant.TenantId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to seed default tenant");
            return false;
        }
    }

    private static async Task SeedDefaultTenantTokenUsageAsync(
        ITokenUsageLimitRepository usageRepository,
        DefaultTenantSettings tenantSettings,
        ILogger logger)
    {
        try
        {
            var existing = await usageRepository.GetTenantLimitAsync(tenantSettings.TenantId);
            if (existing != null)
            {
                logger.LogDebug("Token usage limit already exists for tenant {TenantId}, skipping seeding", tenantSettings.TenantId);
                return;
            }

            var settings = tenantSettings.TokenUsage;
            var limit = new TokenUsageLimit
            {
                TenantId = tenantSettings.TenantId,
                UserId = null,
                MaxTokens = settings.MaxTokens,
                WindowSeconds = settings.WindowSeconds,
                Enabled = true,
                EffectiveFrom = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = "system"
            };

            await usageRepository.UpsertAsync(limit);
            logger.LogInformation("Seeded default token usage limit for tenant {TenantId}", tenantSettings.TenantId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to seed token usage limit for tenant {TenantId}", tenantSettings.TenantId);
        }
    }
    
} 