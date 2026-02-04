using MongoDB.Bson;
using MongoDB.Driver;
using Features.AppsApi.Models;
using Shared.Data;
using Shared.Utils;

namespace Features.AppsApi.Repositories;

public interface IAppIntegrationRepository
{
    /// <summary>
    /// Get an integration by its ID
    /// </summary>
    Task<AppIntegration?> GetByIdAsync(string id);

    /// <summary>
    /// Get all integrations for a tenant
    /// </summary>
    Task<List<AppIntegration>> GetByTenantIdAsync(string tenantId);

    /// <summary>
    /// Get integrations for a specific tenant and platform
    /// </summary>
    Task<List<AppIntegration>> GetByTenantAndPlatformAsync(string tenantId, string platformId);

    /// <summary>
    /// Get integrations for a specific agent activation
    /// </summary>
    Task<List<AppIntegration>> GetByAgentActivationAsync(string tenantId, string agentName, string activationName);

    /// <summary>
    /// Get all enabled integrations for a tenant
    /// </summary>
    Task<List<AppIntegration>> GetEnabledIntegrationsAsync(string tenantId);

    /// <summary>
    /// Check if an integration with the same name exists for the tenant
    /// </summary>
    Task<bool> ExistsByNameAsync(string tenantId, string name, string? excludeId = null);

    /// <summary>
    /// Create a new integration
    /// </summary>
    Task<string> CreateAsync(AppIntegration integration);

    /// <summary>
    /// Update an existing integration
    /// </summary>
    Task<bool> UpdateAsync(string id, AppIntegration integration);

    /// <summary>
    /// Delete an integration
    /// </summary>
    Task<bool> DeleteAsync(string id, string tenantId);

    /// <summary>
    /// Enable or disable an integration
    /// </summary>
    Task<bool> SetEnabledAsync(string id, string tenantId, bool isEnabled);
}

public class AppIntegrationRepository : IAppIntegrationRepository
{
    private readonly IMongoCollection<AppIntegration> _integrations;
    private readonly ILogger<AppIntegrationRepository> _logger;
    private const string CollectionName = "app_integrations";

    public AppIntegrationRepository(IDatabaseService databaseService, ILogger<AppIntegrationRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _integrations = database.GetCollection<AppIntegration>(CollectionName);
        _logger = logger;

        // Ensure indexes are created
        CreateIndexesAsync().Wait();
    }

    private async Task CreateIndexesAsync()
    {
        try
        {
            var indexModels = new List<CreateIndexModel<AppIntegration>>
            {
                // Index for tenant lookups
                new CreateIndexModel<AppIntegration>(
                    Builders<AppIntegration>.IndexKeys.Ascending(x => x.TenantId),
                    new CreateIndexOptions { Name = "idx_tenant_id" }),

                // Compound index for tenant + platform lookups
                new CreateIndexModel<AppIntegration>(
                    Builders<AppIntegration>.IndexKeys
                        .Ascending(x => x.TenantId)
                        .Ascending(x => x.PlatformId),
                    new CreateIndexOptions { Name = "idx_tenant_platform" }),

                // Compound index for agent activation lookups
                new CreateIndexModel<AppIntegration>(
                    Builders<AppIntegration>.IndexKeys
                        .Ascending(x => x.TenantId)
                        .Ascending(x => x.AgentName)
                        .Ascending(x => x.ActivationName),
                    new CreateIndexOptions { Name = "idx_tenant_agent_activation" }),

                // Index for workflow ID lookups
                new CreateIndexModel<AppIntegration>(
                    Builders<AppIntegration>.IndexKeys.Ascending(x => x.WorkflowId),
                    new CreateIndexOptions { Name = "idx_workflow_id" }),

                // Unique index for tenant + name to prevent duplicates
                new CreateIndexModel<AppIntegration>(
                    Builders<AppIntegration>.IndexKeys
                        .Ascending(x => x.TenantId)
                        .Ascending(x => x.Name),
                    new CreateIndexOptions { Name = "idx_tenant_name_unique", Unique = true })
            };

            await _integrations.Indexes.CreateManyAsync(indexModels);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create indexes for {Collection}. They may already exist.", CollectionName);
        }
    }

    public async Task<AppIntegration?> GetByIdAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            if (!ObjectId.TryParse(id, out _))
            {
                return null;
            }
            return await _integrations.Find(x => x.Id == id).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAppIntegrationById");
    }

    public async Task<List<AppIntegration>> GetByTenantIdAsync(string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _integrations
                .Find(x => x.TenantId == tenantId)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAppIntegrationsByTenantId");
    }

    public async Task<List<AppIntegration>> GetByTenantAndPlatformAsync(string tenantId, string platformId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _integrations
                .Find(x => x.TenantId == tenantId && x.PlatformId == platformId.ToLowerInvariant())
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAppIntegrationsByTenantAndPlatform");
    }

    public async Task<List<AppIntegration>> GetByAgentActivationAsync(string tenantId, string agentName, string activationName)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _integrations
                .Find(x => x.TenantId == tenantId && 
                           x.AgentName == agentName && 
                           x.ActivationName == activationName)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAppIntegrationsByAgentActivation");
    }

    public async Task<List<AppIntegration>> GetEnabledIntegrationsAsync(string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _integrations
                .Find(x => x.TenantId == tenantId && x.IsEnabled)
                .SortByDescending(x => x.CreatedAt)
                .ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetEnabledAppIntegrations");
    }

    public async Task<bool> ExistsByNameAsync(string tenantId, string name, string? excludeId = null)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<AppIntegration>.Filter.And(
                Builders<AppIntegration>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<AppIntegration>.Filter.Eq(x => x.Name, name)
            );

            if (!string.IsNullOrEmpty(excludeId))
            {
                filter = Builders<AppIntegration>.Filter.And(
                    filter,
                    Builders<AppIntegration>.Filter.Ne(x => x.Id, excludeId)
                );
            }

            var count = await _integrations.CountDocumentsAsync(filter);
            return count > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CheckAppIntegrationExistsByName");
    }

    public async Task<string> CreateAsync(AppIntegration integration)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            integration.Id = ObjectId.GenerateNewId().ToString();
            integration.GenerateWorkflowId();

            await _integrations.InsertOneAsync(integration);
            
            _logger.LogInformation("Created app integration {IntegrationId} for tenant {TenantId}", 
                integration.Id, integration.TenantId);
            
            return integration.Id;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateAppIntegration");
    }

    public async Task<bool> UpdateAsync(string id, AppIntegration integration)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            integration.UpdatedAt = DateTime.UtcNow;
            integration.GenerateWorkflowId();

            var result = await _integrations.ReplaceOneAsync(
                x => x.Id == id && x.TenantId == integration.TenantId,
                integration);

            var success = result.ModifiedCount > 0;
            
            if (success)
            {
                _logger.LogInformation("Updated app integration {IntegrationId}", id);
            }
            else
            {
                _logger.LogWarning("Failed to update app integration {IntegrationId} - not found or tenant mismatch", id);
            }

            return success;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateAppIntegration");
    }

    public async Task<bool> DeleteAsync(string id, string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var result = await _integrations.DeleteOneAsync(
                x => x.Id == id && x.TenantId == tenantId);
            
            var success = result.DeletedCount > 0;
            
            if (success)
            {
                _logger.LogInformation("Deleted app integration {IntegrationId}", id);
            }
            else
            {
                _logger.LogWarning("Failed to delete app integration {IntegrationId} - not found or tenant mismatch", id);
            }

            return success;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteAppIntegration");
    }

    public async Task<bool> SetEnabledAsync(string id, string tenantId, bool isEnabled)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var update = Builders<AppIntegration>.Update
                .Set(x => x.IsEnabled, isEnabled)
                .Set(x => x.UpdatedAt, DateTime.UtcNow);

            var result = await _integrations.UpdateOneAsync(
                x => x.Id == id && x.TenantId == tenantId,
                update);

            var success = result.ModifiedCount > 0;
            
            if (success)
            {
                _logger.LogInformation("Set app integration {IntegrationId} enabled={IsEnabled}", id, isEnabled);
            }
            else
            {
                _logger.LogWarning("Failed to set enabled status for app integration {IntegrationId}", id);
            }

            return success;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "SetAppIntegrationEnabled");
    }
}
