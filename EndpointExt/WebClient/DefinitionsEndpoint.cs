using MongoDB.Bson;
using XiansAi.Server.MongoDB;
using XiansAi.Server.MongoDB.Models;
using XiansAi.Server.MongoDB.Repositories;
using XiansAi.Server.Utils;

namespace XiansAi.Server.EndpointExt.WebClient;

public class DefinitionsEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<DefinitionsEndpoint> _logger;

    public DefinitionsEndpoint(
        IDatabaseService databaseService,
        ILogger<DefinitionsEndpoint> logger
    )
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task<IResult> GetLatestDefinitions()
    {
        var definitionRepository = new FlowDefinitionRepository(await _databaseService.GetDatabase());
        var definitions = await definitionRepository.GetLatestDefinitionsAsync();
        _logger.LogInformation("Found {Count} definitions", definitions.Count);
        return Results.Ok(definitions);
    }
    

}