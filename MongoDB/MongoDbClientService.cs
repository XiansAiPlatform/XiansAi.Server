using MongoDB.Driver;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace XiansAi.Server.MongoDB;

public interface IMongoDbClientService
{
    Task<IMongoDatabase> GetDatabase();
    Task<IMongoCollection<T>> GetCollection<T>(string collectionName);
    Task<IMongoClient> GetClient();
}

public class MongoDbClientService : IMongoDbClientService
{
    private IMongoClient?_mongoClient;
    public MongoDBConfig Config { get; init; }
    private readonly IConfiguration _configuration;

    private readonly IKeyVaultService _keyVaultService;


    public MongoDbClientService(MongoDBConfig config, IKeyVaultService keyVaultService, IConfiguration configuration)
    {
        Config = config;
        _configuration = configuration;
        _keyVaultService = keyVaultService;
    }

    public async Task<IMongoDatabase> GetDatabase()
    {
        var mongoClient = await GetClient();
        return mongoClient.GetDatabase(Config.DatabaseName);
    }

    public async Task<IMongoCollection<T>> GetCollection<T>(string collectionName)
    {
        var database = await GetDatabase();
        return database.GetCollection<T>(collectionName);
    }

    public async Task<IMongoClient> GetClient()
    {
        if (_mongoClient == null)
        {
            var connectionString = Config.ConnectionString;
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            var cert = await GetCertificateAsync();
            settings.SslSettings = new SslSettings
            {
                ClientCertificates = new List<X509Certificate>() { cert }
            };
            _mongoClient = new MongoClient(settings);
        }
        return _mongoClient;
    }

    private async Task<X509Certificate2> GetCertificateAsync()
    {
        // read from local file system. User for local development
        if (Config.CertificateFilePath != null && Config.CertificateFilePassword != null)
        {
#pragma warning disable SYSLIB0057 // Type or member is obsolete
            return new X509Certificate2(Config.CertificateFilePath, Config.CertificateFilePassword);
#pragma warning restore SYSLIB0057 // Type or member is obsolete        
        } else if (Config.CertificateKeyVaultName != null)
        {
            var cert = await _keyVaultService.LoadFromKeyVault(Config.CertificateKeyVaultName);
            return cert;
        } else {
            throw new Exception("CertificateFilePath and CertificateFilePassword are not set");
        }
    }
}

