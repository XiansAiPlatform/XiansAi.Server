using MongoDB.Bson;
using XiansAi.Server.Auth;
using XiansAi.Server.MongoDB;
using XiansAi.Server.MongoDB.Models;
using XiansAi.Server.MongoDB.Repositories;
using XiansAi.Server.Utils;

namespace XiansAi.Server.EndpointExt.WebClient;

public class DefinitionsEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<DefinitionsEndpoint> _logger;
    private readonly ITenantContext _tenantContext;
    public DefinitionsEndpoint(
        IDatabaseService databaseService,
        ILogger<DefinitionsEndpoint> logger,
        ITenantContext tenantContext
    )
    {
        _databaseService = databaseService;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task<IResult> GetLatestDefinitions(DateTime? startTime, DateTime? endTime, string? owner)
    {
        if (owner == "current" && _tenantContext.LoggedInUser == null)
        {
            return Results.Unauthorized();
        }

        if (owner == "current")
        {
            owner = _tenantContext.LoggedInUser;
        }

        var definitionRepository = new FlowDefinitionRepository(await _databaseService.GetDatabase());
        var definitions = await definitionRepository.GetLatestDefinitionsAsync(startTime, endTime, owner);
        _logger.LogInformation("Found {Count} definitions", definitions.Count);
        return Results.Ok(definitions);
    }
    

}