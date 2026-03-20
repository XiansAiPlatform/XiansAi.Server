using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Microsoft.Extensions.Logging;

namespace Shared.Data;

public interface IMongoDbClientService
{
    IMongoDatabase GetDatabase();
    IMongoCollection<T> GetCollection<T>(string collectionName);
    IMongoClient GetClient();
}

public class MongoDbClientService : IMongoDbClientService, IDisposable
{
    public IMongoDBConfig Config { get; init; }
    private readonly IMongoClient _mongoClient;
    private readonly ILogger<MongoDbClientService> _logger;
    private readonly IConfiguration _configuration;
    private bool _disposed = false;

    public MongoDbClientService(
        IMongoDbContext context,
        ILogger<MongoDbClientService> logger,
        IConfiguration configuration)
    {
        Config = context.GetMongoDBConfig();
        _logger = logger;
        _configuration = configuration;
        _mongoClient = CreateMongoClient();
        
        _logger.LogInformation("MongoDB client initialized with connection to {DatabaseName}", Config.DatabaseName);
    }

    public IMongoDatabase GetDatabase()
    {
        ThrowIfDisposed();
        return _mongoClient.GetDatabase(Config.DatabaseName);
    }

    public IMongoCollection<T> GetCollection<T>(string collectionName)
    {
        ThrowIfDisposed();
        var database = GetDatabase();
        return database.GetCollection<T>(collectionName);
    }

    public IMongoClient GetClient()
    {
        ThrowIfDisposed();
        return _mongoClient;
    }

    private IMongoClient CreateMongoClient()
    {
        var connectionString = Config.ConnectionString;
        var settings = MongoClientSettings.FromConnectionString(connectionString);
        
        // Configure connection pool settings from configuration
        settings.MaxConnectionPoolSize = Config.MaxConnectionPoolSize;
        settings.MinConnectionPoolSize = Config.MinConnectionPoolSize;
        settings.MaxConnectionIdleTime = Config.MaxConnectionIdleTime;
        settings.WaitQueueTimeout = Config.WaitQueueTimeout;
        
        // Configure operation timeouts from configuration
        settings.ConnectTimeout = Config.ConnectTimeout;
        settings.SocketTimeout = Config.SocketTimeout;
        settings.ServerSelectionTimeout = Config.ServerSelectionTimeout;
        
        // Configure heartbeat settings from configuration
        settings.HeartbeatInterval = Config.HeartbeatInterval;
        settings.HeartbeatTimeout = Config.HeartbeatTimeout;
        
        // Configure retry settings from configuration
        settings.RetryWrites = Config.RetryWrites;
        settings.RetryReads = Config.RetryReads;

        // Register the DiagnosticsActivityEventSubscriber so the driver emits Activity spans
        // under the "MongoDB.Driver.Core.Extensions.DiagnosticSources" ActivitySource.
        // Primary configuration keys:
        // - OpenTelemetry:MongoDB:ExcludedCommands (CSV)
        // - OpenTelemetry:MongoDB:CaptureCommandText (bool)
        // Fallback keys preserve compatibility with existing deployments.
        var excludedCommandsValue =
            _configuration.GetValue<string>("OpenTelemetry:MongoDB:ExcludedCommands")
            ?? _configuration.GetValue<string>("OPENTELEMETRY_MONGODB_EXCLUDED_COMMANDS");

        var excludedCommands = string.IsNullOrWhiteSpace(excludedCommandsValue)
            ? null
            : excludedCommandsValue
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // CaptureCommandText is opt-in — raw MongoDB query documents can contain tenant data or
        // PII (e.g. filter values, $match fields). Enable only in trusted dev/internal environments.
        var captureCommandText =
            _configuration.GetValue<bool?>("OpenTelemetry:MongoDB:CaptureCommandText")
            ?? _configuration.GetValue<bool?>("OPENTELEMETRY_MONGODB_CAPTURE_COMMAND_TEXT")
            ?? false;

        var otelEnabled = _configuration.GetValue<bool>("OpenTelemetry:Enabled", false);
        if (otelEnabled)
        {
            settings.ClusterConfigurator = cb =>
                cb.Subscribe(new DiagnosticsActivityEventSubscriber(new InstrumentationOptions
                {
                    CaptureCommandText = captureCommandText,
                    ShouldStartActivity = @event => excludedCommands == null || !excludedCommands.Contains(@event.CommandName)
                }));
        }
        
        _logger.LogDebug("Creating MongoDB client with connection pool: Max={MaxPool}, Min={MinPool}, IdleTimeout={IdleTimeout}, RetryWrites={RetryWrites}, RetryReads={RetryReads}", 
            settings.MaxConnectionPoolSize, settings.MinConnectionPoolSize, settings.MaxConnectionIdleTime, settings.RetryWrites, settings.RetryReads);
        
        return new MongoClient(settings);
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MongoDbClientService));
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                _logger.LogInformation("Disposing MongoDB client");
                // MongoClient implements IDisposable starting from driver version 2.14+
                // This will properly close all connections in the pool
                if (_mongoClient is IDisposable disposableClient)
                {
                    disposableClient.Dispose();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing MongoDB client");
            }
            finally
            {
                _disposed = true;
            }
        }
    }
}

