using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Data;

namespace XiansAi.Server.Features.WebApi.Scripts;

public class CreateIndexes
{
    public static async Task CreateDefinitionIndexes(IDatabaseService databaseService)
    {
        var database = await databaseService.GetDatabase();
        var collection = database.GetCollection<FlowDefinition>("flow_definitions");

        // Create index for sorting by creation date
        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<FlowDefinition>(
                Builders<FlowDefinition>.IndexKeys.Descending(x => x.CreatedAt)
            )
        );

        // Create index for agent field (used for querying by agent)
        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<FlowDefinition>(
                Builders<FlowDefinition>.IndexKeys.Ascending(x => x.Agent)
            )
        );

        // Create index for workflow type
        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<FlowDefinition>(
                Builders<FlowDefinition>.IndexKeys.Ascending(x => x.WorkflowType)
            )
        );

        Console.WriteLine("Indexes created successfully!");
    }
} 