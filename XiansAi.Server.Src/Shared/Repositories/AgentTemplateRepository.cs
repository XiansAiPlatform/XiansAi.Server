using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

/// <summary>
/// Repository for managing agent templates (system-scoped reusable agent definitions).
/// </summary>
public class AgentTemplateRepository : IAgentTemplateRepository
{
    private readonly IMongoCollection<AgentTemplate> _templates;
    private readonly ILogger<AgentTemplateRepository> _logger;

    public AgentTemplateRepository(IDatabaseService databaseService, ILogger<AgentTemplateRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _templates = database.GetCollection<AgentTemplate>("agent_templates");
        _logger = logger;
    }

    public async Task<AgentTemplate?> GetByNameAsync(string name)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _templates.Find(x => x.Name == name).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAgentTemplateByName");
    }

    public async Task<List<AgentTemplate>> GetAllAsync()
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _templates.Find(_ => true).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAllAgentTemplates");
    }

    public async Task<List<AgentTemplate>> GetByCategoryAsync(string category)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // Filter by category in metadata
            var filter = Builders<AgentTemplate>.Filter.Eq("metadata.category", category);
            return await _templates.Find(filter).ToListAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAgentTemplatesByCategory");
    }

    public async Task CreateAsync(AgentTemplate template)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _templates.InsertOneAsync(template);
            _logger.LogInformation("Created agent template {TemplateName} with ID {TemplateId}", template.Name, template.Id);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateAgentTemplate");
    }

    public async Task<bool> UpdateAsync(string id, AgentTemplate template)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var result = await _templates.ReplaceOneAsync(x => x.Id == id, template);
            if (result.ModifiedCount > 0)
            {
                _logger.LogInformation("Updated agent template {TemplateId}", id);
            }
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateAgentTemplate");
    }

    public async Task<bool> DeleteAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var result = await _templates.DeleteOneAsync(x => x.Id == id);
            if (result.DeletedCount > 0)
            {
                _logger.LogInformation("Deleted agent template {TemplateId}", id);
            }
            return result.DeletedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteAgentTemplate");
    }

    public async Task<bool> ExistsAsync(string name)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var count = await _templates.CountDocumentsAsync(x => x.Name == name);
            return count > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "AgentTemplateExists");
    }

    public async Task<AgentTemplate?> GetByIdAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _templates.Find(x => x.Id == id).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAgentTemplateById");
    }
}



