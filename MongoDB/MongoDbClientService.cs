using MongoDB.Driver;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Collections.Concurrent;
using XiansAi.Server.Auth;

namespace XiansAi.Server.MongoDB;

public interface IMongoDbClientService
{
    IMongoDatabase GetDatabase();
    IMongoCollection<T> GetCollection<T>(string collectionName);
    IMongoClient GetClient();
}

public class MongoDbClientService : IMongoDbClientService
{
    private static readonly ConcurrentDictionary<string, IMongoClient> _mongoClients = new();
    public MongoDBConfig Config { get; init; }
    private readonly ITenantContext _tenantContext;
    private readonly IKeyVaultService _keyVaultService;


    public MongoDbClientService(MongoDBConfig config, IKeyVaultService keyVaultService, ITenantContext tenantContext)
    {
        Config = config;
        _keyVaultService = keyVaultService;
        _tenantContext = tenantContext;
    }

    public IMongoDatabase GetDatabase()
    {
        var mongoClient = GetClient();
        return mongoClient.GetDatabase(Config.DatabaseName);
    }

    public IMongoCollection<T> GetCollection<T>(string collectionName)
    {
        var database = GetDatabase();
        return database.GetCollection<T>(collectionName);
    }

    public IMongoClient GetClient()
    {
        var tenantId = _tenantContext.TenantId;
        return _mongoClients.GetOrAdd(tenantId, _ => 
        {
            var connectionString = Config.ConnectionString;
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            var cert = GetCertificateAsync()?.GetAwaiter().GetResult();
            if (cert != null) {
                settings.SslSettings = new SslSettings
                {
                    ClientCertificates = new List<X509Certificate>() { cert }
                };
            }
            return new MongoClient(settings);
        });
    }

    private async Task<X509Certificate2?> GetCertificateAsync()
    {
        // read from local file system. User for local development
        if (Config.CertificateFilePath != null && Config.CertificateFilePassword != null)
        {
#pragma warning disable SYSLIB0057 // Type or member is obsolete
            return new X509Certificate2(Config.CertificateFilePath, Config.CertificateFilePassword);
#pragma warning restore SYSLIB0057 // Type or member is obsolete        
        } else if (Config.CertificateKeyVaultName != null)
        {
            var cert = await _keyVaultService.LoadCertificate(Config.CertificateKeyVaultName);
            return cert;
        } 
        return null;
    }
}

