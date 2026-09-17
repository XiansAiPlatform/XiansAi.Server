using Features.AgentApi.Repositories;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Moq;
using Shared.Data;
using Shared.Data.Models;
using Shared.Services;

namespace XiansAi.Server.Tests.UnitTests.Features.Mcp;

public class DataDeletionScopeTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AdminDeletionPreservesExplicitIdsIncludingEmptySnapshot(bool empty)
    {
        var repository = new Mock<IDocumentRepository>();
        var ids = new List<string>();
        if (!empty) ids.Add("0123456789abcdef01234567");
        repository.Setup(x => x.DeleteByFilterAsync("tenant", It.IsAny<DocumentQueryFilter>())).ReturnsAsync(ids.Count);
        var service = new AdminDataService(repository.Object, Mock.Of<ILogger<AdminDataService>>());
        var start = DateTime.UtcNow.AddDays(-1);
        var end = DateTime.UtcNow;
        var result = await service.DeleteDataAsync(new AdminDataDeleteRequest
        {
            TenantId = "tenant", AgentName = "agent", ActivationName = "activation", DataType = "reports",
            StartDate = start, EndDate = end, RecordIds = ids
        });
        Assert.True(result.IsSuccess);
        Assert.Equal(ids.Count, result.Data!.DeletedCount);
        repository.Verify(x => x.DeleteByFilterAsync("tenant", It.Is<DocumentQueryFilter>(f =>
            f.Ids != null && f.Ids.SequenceEqual(ids) && f.AgentId == "agent" && f.ActivationName == "activation" &&
            f.Type == "reports" && f.CreatedAfter == start && f.CreatedBefore == end)), Times.Once);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MongoDeletionFilterIncludesIdsAndExactScope(bool empty)
    {
        var collection = new Mock<IMongoCollection<Document>>();
        var database = new Mock<IMongoDatabase>();
        database.Setup(x => x.GetCollection<Document>("documents", It.IsAny<MongoCollectionSettings>())).Returns(collection.Object);
        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabaseAsync()).ReturnsAsync(database.Object);
        FilterDefinition<Document>? captured = null;
        collection.Setup(x => x.DeleteManyAsync(It.IsAny<FilterDefinition<Document>>(), It.IsAny<CancellationToken>()))
            .Callback<FilterDefinition<Document>, CancellationToken>((filter, _) => captured = filter)
            .ReturnsAsync(new DeleteResult.Acknowledged(0));
        var repository = new DocumentRepository(databaseService.Object, Mock.Of<ILogger<DocumentRepository>>());
        var ids = new List<string>();
        if (!empty) ids.Add("0123456789abcdef01234567");
        await repository.DeleteByFilterAsync("tenant", new DocumentQueryFilter
            { Ids = ids, AgentId = "agent", ActivationName = "activation", Type = "reports" });
        Assert.NotNull(captured);
        var filter = captured.Render(new RenderArgs<Document>(BsonSerializer.LookupSerializer<Document>(), BsonSerializer.SerializerRegistry));
        Assert.Equal("tenant", filter["tenant_id"].AsString);
        Assert.Equal("agent", filter["agent_id"].AsString);
        Assert.Equal("activation", filter["activation_name"].AsString);
        Assert.Equal("reports", filter["type"].AsString);
        Assert.Equal(ids.Select(ObjectId.Parse), filter["_id"]["$in"].AsBsonArray.Select(value => value.AsObjectId));
    }
}
