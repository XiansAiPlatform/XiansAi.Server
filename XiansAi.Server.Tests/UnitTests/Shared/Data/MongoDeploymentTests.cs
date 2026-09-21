using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Moq;
using Shared.Data;
using Xunit;

namespace Tests.UnitTests.Shared.Data;

/// <summary>
/// The change stream check disables live message push for the life of the process when it answers
/// false, so these pin down that only a positively identified standalone mongod gets that answer.
/// </summary>
public class MongoDeploymentTests
{
    private static readonly BsonDocument StandaloneHello = new()
    {
        { "isWritablePrimary", true },
        { "maxBsonObjectSize", 16777216 },
        { "ok", 1 }
    };

    private static readonly BsonDocument MongoDbBuildInfo = new()
    {
        { "version", "7.0.14" },
        { "gitVersion", "0f4b7ae0e92e5d3b7e4a7e1c1b0b9a9b7e2d1f30" },
        { "ok", 1 }
    };

    [Fact]
    public async Task ReplicaSetMember_SupportsChangeStreams()
    {
        var hello = new BsonDocument(StandaloneHello) { { "setName", "rs0" } };
        var database = BuildDatabase(hello, MongoDbBuildInfo);

        Assert.True(await Act(database));
    }

    [Fact]
    public async Task Mongos_SupportsChangeStreams()
    {
        var hello = new BsonDocument(StandaloneHello) { { "msg", "isdbgrid" } };
        var database = BuildDatabase(hello, MongoDbBuildInfo);

        Assert.True(await Act(database));
    }

    [Fact]
    public async Task StandaloneMongod_DoesNotSupportChangeStreams()
    {
        var database = BuildDatabase(StandaloneHello, MongoDbBuildInfo);

        Assert.False(await Act(database));
    }

    [Fact]
    public async Task ConfiguredDocumentDbProvider_SupportsChangeStreamsWithoutProbing()
    {
        var database = BuildDatabase(StandaloneHello, MongoDbBuildInfo);

        Assert.True(await Act(database, MongoProvider.DocumentDB));

        database.Verify(
            d => d.RunCommandAsync(
                It.IsAny<Command<BsonDocument>>(),
                It.IsAny<ReadPreference>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CosmosDb_SupportsChangeStreamsDespiteNotReportingAReplicaSet()
    {
        var cosmosBuildInfo = new BsonDocument { { "version", "4.2.0" }, { "_t", "DocumentDB" }, { "ok", 1 } };
        var database = BuildDatabase(StandaloneHello, cosmosBuildInfo);

        Assert.True(await Act(database));
    }

    [Fact]
    public async Task ServerWithoutTopologyCommands_SupportsChangeStreams()
    {
        var database = BuildDatabase(CommandRejected(), MongoDbBuildInfo);

        Assert.True(await Act(database));
    }

    [Fact]
    public async Task ServerThatRevealsNothing_FallsBackToTheEndpointItIsHostedOn()
    {
        var database = BuildDatabase(StandaloneHello, CommandRejected(), "my-account.mongo.cosmos.azure.com");

        Assert.True(await Act(database));
    }

    private static Task<bool> Act(Mock<IMongoDatabase> database, MongoProvider provider = MongoProvider.MongoDB)
    {
        return MongoDeployment.SupportsChangeStreamsAsync(
            database.Object, provider, NullLogger.Instance, CancellationToken.None);
    }

    private static MongoCommandException CommandRejected()
    {
        var connectionId = new ConnectionId(new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));
        return new MongoCommandException(
            connectionId,
            "Command is not supported",
            new BsonDocument(),
            new BsonDocument { { "ok", 0 }, { "code", 59 } });
    }

    /// <summary>
    /// A database whose topology command answers <paramref name="helloResponse"/> (a
    /// <see cref="MongoCommandException"/> makes it reject the command instead) and whose admin
    /// database answers <paramref name="buildInfoResponse"/> the same way.
    /// </summary>
    private static Mock<IMongoDatabase> BuildDatabase(
        object helloResponse,
        object buildInfoResponse,
        string host = "localhost")
    {
        var adminDatabase = new Mock<IMongoDatabase>();
        SetupRunCommand(adminDatabase, buildInfoResponse);

        var client = new Mock<IMongoClient>();
        client.Setup(c => c.GetDatabase("admin", It.IsAny<MongoDatabaseSettings>())).Returns(adminDatabase.Object);
        client.SetupGet(c => c.Settings).Returns(new MongoClientSettings
        {
            Server = new MongoServerAddress(host, 27017)
        });

        var database = new Mock<IMongoDatabase>();
        SetupRunCommand(database, helloResponse);
        database.SetupGet(d => d.Client).Returns(client.Object);
        return database;
    }

    private static void SetupRunCommand(Mock<IMongoDatabase> database, object response)
    {
        var setup = database.Setup(d => d.RunCommandAsync(
            It.IsAny<Command<BsonDocument>>(),
            It.IsAny<ReadPreference>(),
            It.IsAny<CancellationToken>()));

        if (response is Exception exception)
        {
            setup.ThrowsAsync(exception);
        }
        else
        {
            setup.ReturnsAsync((BsonDocument)response);
        }
    }
}
