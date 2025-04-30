using MongoDB.Driver;
using XiansAi.Server.Shared.Data;
using Shared.Data.Models;

namespace XiansAi.Server.Features.WebApi.Scripts;

public class CreateIndexes
{
    public static async Task CreateDefinitionIndexes(IDatabaseService databaseService)
    {
        var database = await databaseService.GetDatabase();
        var collection = database.GetCollection<FlowDefinition>("flow_definitions");

        // Create indexes for permission fields
        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<FlowDefinition>(
                Builders<FlowDefinition>.IndexKeys.Ascending(x => x.Permissions.ReadAccess)
            )
        );

        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<FlowDefinition>(
                Builders<FlowDefinition>.IndexKeys.Ascending(x => x.Permissions.WriteAccess)
            )
        );

        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<FlowDefinition>(
                Builders<FlowDefinition>.IndexKeys.Ascending(x => x.Permissions.OwnerAccess)
            )
        );

        // Create index for sorting by creation date
        await collection.Indexes.CreateOneAsync(
            new CreateIndexModel<FlowDefinition>(
                Builders<FlowDefinition>.IndexKeys.Descending(x => x.CreatedAt)
            )
        );

        Console.WriteLine("Indexes created successfully!");
    }
} 