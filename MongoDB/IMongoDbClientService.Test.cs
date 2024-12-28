using DotNetEnv;
using Xunit;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;

public class MongoDbClientServiceTests
{
    private readonly MongoDbClientService _sut;
    
    public MongoDbClientServiceTests()
    {
        Env.Load();
        var config = new MongoDBConfig
        {
            ConnectionString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING") ?? 
                throw new InvalidOperationException("MONGODB_CONNECTION_STRING environment variable is required"),
            DatabaseName = Environment.GetEnvironmentVariable("MONGODB_DATABASE_NAME") ?? 
                throw new InvalidOperationException("MONGODB_DATABASE_NAME environment variable is required"),
            PfxPath = Environment.GetEnvironmentVariable("MONGODB_PFX_PATH") ?? 
                throw new InvalidOperationException("MONGODB_PFX_PATH environment variable is required"),
            PfxPassphrase = Environment.GetEnvironmentVariable("MONGODB_PFX_PASSPHRASE") ?? 
                throw new InvalidOperationException("MONGODB_PFX_PASSPHRASE environment variable is required")
        };
        
        _sut = new MongoDbClientService(config);
    }

    /*
    dotnet test Flowmaxer.Common.csproj --filter "FullyQualifiedName~MongoDbClientServiceTests"
    */
    [Fact]
    public void GetCollection_ShouldReturnMongoCollection_WhenValidCollectionNameProvided()
    {
        // Arrange
        const string collectionName = "definitions";

        // Act
        var collection = _sut.GetCollection<Definition>(collectionName);

        

        // Assert
        Assert.NotNull(collection);
    }

    [Fact]
    public async Task InsertDocument_ShouldSuccessfullyInsertDocument()
    {
        // Arrange
        const string collectionName = "test_collection";
        var testDoc = new Definition { Id = ObjectId.GenerateNewId(), Name = "Test Definition" };
        var collection = _sut.GetCollection<Definition>(collectionName);

        try
        {
            // Act
            await collection.InsertOneAsync(testDoc);

            // Assert
            var result = await collection.Find(d => d.Id == testDoc.Id).FirstOrDefaultAsync();
            Assert.NotNull(result);
            Assert.Equal(testDoc.Name, result.Name);
        }
        finally
        {
            // Cleanup
            //await collection.DeleteOneAsync(d => d.Id == testDoc.Id);
        }
    }

}

