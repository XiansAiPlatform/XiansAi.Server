using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Data;

namespace Features.WebApi.Repositories;

public interface IFlowDefinitionRepository
{
    //Task<bool> IfExistsInAnotherOwner(string typeName, string owner);
    //Task<long> DeleteByOwnerAndTypeNameAsync(string owner, string typeName);
    Task<FlowDefinition> GetLatestFlowDefinitionAsync(string workflowType);
    Task<FlowDefinition> GetByIdAsync(string id);
    Task<FlowDefinition> GetByHashAsync(string hash, string workflowType);
    Task<List<FlowDefinition>> GetByNameAsync(string name);
    Task<List<FlowDefinition>> GetAllAsync();
    Task CreateAsync(FlowDefinition definition);
    Task<bool> DeleteAsync(string id);
    Task<bool> UpdateAsync(string id, FlowDefinition definition);
    Task<FlowDefinition> GetByNameHashAsync(string workflowType, string hash);
    Task<List<FlowDefinition>> GetDefinitionsWithPermissionAsync(string userId, DateTime? startTime, DateTime? endTime, bool basicDataOnly = false);
    Task<List<string>> GetAgentsWithPermissionAsync(string userId);
}

public class FlowDefinitionRepository : IFlowDefinitionRepository
{
    private readonly IMongoCollection<FlowDefinition> _definitions;

    private readonly ILogger<FlowDefinitionRepository> _logger ;

    public FlowDefinitionRepository(IDatabaseService databaseService, ILogger<FlowDefinitionRepository> logger)
    {
        var database = databaseService.GetDatabase().Result;
        _definitions = database.GetCollection<FlowDefinition>("flow_definitions");
        _logger = logger;
    }

    public async Task<FlowDefinition> GetLatestFlowDefinitionAsync(string workflowType)
    {
        return await _definitions.Find(x => x.WorkflowType == workflowType)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetByIdAsync(string id)
    {
        return await _definitions.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<FlowDefinition> GetByHashAsync(string hash, string workflowType)
    {
        return await _definitions.Find(x => x.Hash == hash && x.WorkflowType == workflowType).FirstOrDefaultAsync();
    }

    public async Task<List<FlowDefinition>> GetByNameAsync(string name)
    {
        return await _definitions.Find(x => x.WorkflowType == name)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<FlowDefinition>> GetAllAsync()
    {
        return await _definitions.Find(_ => true).ToListAsync();
    }

    public async Task CreateAsync(FlowDefinition definition)
    {
        definition.CreatedAt = DateTime.UtcNow;
        await _definitions.InsertOneAsync(definition);
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _definitions.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }

    public async Task<bool> UpdateAsync(string id, FlowDefinition definition)
    {
        var result = await _definitions.ReplaceOneAsync(x => x.Id == id, definition);
        return result.ModifiedCount > 0;
    }

    public async Task<FlowDefinition> GetByNameHashAsync(string workflowType, string hash)
    {
        return await _definitions.Find(x => x.WorkflowType == workflowType && x.Hash == hash)
            .FirstOrDefaultAsync();
    }

    public async Task<List<string>> GetAgentsWithPermissionAsync(string userId)
    {
        var definitions = await GetDefinitionsWithPermissionAsync(userId, null, null, basicDataOnly: true);
        return definitions.Select(x => x.Agent).Distinct().ToList();
    }

    public async Task<List<FlowDefinition>> GetDefinitionsWithPermissionAsync(string userId, DateTime? startTime, DateTime? endTime, bool basicDataOnly = false)
    {
        _logger.LogInformation("Getting definitions with permission for user: {UserId} and start time: {StartTime} and end time: {EndTime}", userId, startTime, endTime);

        var filterBuilder = Builders<FlowDefinition>.Filter;
        
        // Create permission filter using the simplified model
        var permissionFilters = new List<FilterDefinition<FlowDefinition>>
        {
            // Check if user is owner
            filterBuilder.AnyEq("permissions.owner_access", userId),
            // Check if user has read access
            filterBuilder.AnyEq("permissions.read_access", userId),
            // Check if user has write access (which includes read)
            filterBuilder.AnyEq("permissions.write_access", userId)
        };

        var permissionFilter = filterBuilder.Or(permissionFilters);

        // Create time filter
        var timeFilter = filterBuilder.And(
            startTime == null ? filterBuilder.Empty : filterBuilder.Gte(x => x.UpdatedAt, startTime.Value),
            endTime == null ? filterBuilder.Empty : filterBuilder.Lte(x => x.UpdatedAt, endTime.Value)
        );

        // Combine filters
        var finalFilter = filterBuilder.And(permissionFilter, timeFilter);

        var findFluent = _definitions.Find(finalFilter).SortByDescending(x => x.UpdatedAt);

        if (basicDataOnly)
        {
            return await findFluent
                .Project<FlowDefinition>(Builders<FlowDefinition>.Projection
                    .Include(x => x.Agent)
                    .Include(x => x.WorkflowType)
                    .Include(x => x.CreatedAt)
                    .Include(x => x.UpdatedAt))
                .ToListAsync();
        }

        return await findFluent
            .Project<FlowDefinition>(Builders<FlowDefinition>.Projection
                .Include(x => x.Agent)
                .Include(x => x.WorkflowType)
                .Include(x => x.CreatedAt)
                .Include(x => x.Permissions)
                .Include(x => x.ParameterDefinitions)
                .Include(x => x.ActivityDefinitions)
                .Include(x => x.Id)
                .Include(x => x.Source)
                .Include(x => x.Markdown)
                .Include(x => x.WorkflowType)
                .Include(x => x.UpdatedAt))
            .ToListAsync();
    }
}


