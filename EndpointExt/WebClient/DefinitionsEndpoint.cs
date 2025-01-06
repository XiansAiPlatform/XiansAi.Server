using MongoDB.Bson;
using XiansAi.Server.MongoDB;
using XiansAi.Server.MongoDB.Models;
using XiansAi.Server.MongoDB.Repositories;
using XiansAi.Server.Utils;

namespace XiansAi.Server.EndpointExt.WebClient;

public class DefinitionsEndpoint
{
    private readonly FlowDefinitionRepository _flowDefinitionRepository;
    private readonly ILogger<DefinitionsEndpoint> _logger;

    public DefinitionsEndpoint(
        IMongoDbClientService mongoDbClientService,
        ILogger<DefinitionsEndpoint> logger
    )
    {
        var database = mongoDbClientService.GetDatabase();
        _flowDefinitionRepository = new FlowDefinitionRepository(database);
        _logger = logger;
    }

    public async Task<IResult> GetLatestDefinitions()
    {
        var definitions = await _flowDefinitionRepository.GetLatestDefinitionsForAllTypesAsync();

        foreach (var definition in definitions)
        {
            definition.Source = null;
        }
        _logger.LogInformation("Found {Count} definitions", definitions.Count);
        return Results.Ok(definitions);
    }

}