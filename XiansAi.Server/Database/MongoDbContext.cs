using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using Shared.Auth;

namespace XiansAi.Server.Database
{
    public interface IMongoDbContext
    {
        MongoDBConfig GetMongoDBConfig();
    }

    public class MongoDbContext : IMongoDbContext
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<MongoDbContext> _logger;
        private readonly ITenantContext _tenantContext;

        public MongoDbContext(IConfiguration configuration, ILogger<MongoDbContext> logger, ITenantContext tenantContext)
        {
            _configuration = configuration;
            _logger = logger;
            _tenantContext = tenantContext;
        }

        public MongoDBConfig GetMongoDBConfig()
        {
            if (string.IsNullOrEmpty(_tenantContext.TenantId))
                throw new InvalidOperationException("TenantId is required");

            // get the mongo config for the tenant
            var mongoConfig = _configuration.GetSection($"Tenants:{_tenantContext.TenantId}:MongoDB").Get<MongoDBConfig>();

            // if the mongo config is not found, use the default values
            if (mongoConfig == null)
            {
                _logger.LogInformation("MongoDB configuration for tenant {TenantId} not found. Using default values.", _tenantContext.TenantId);

                var connectionString = _configuration.GetSection("MongoDB:ConnectionString").Value
                    ?? throw new InvalidOperationException("MongoDB connection string not found");
                var databaseName = _tenantContext.TenantId;
                
                // create a new mongo config with default values
                mongoConfig = new MongoDBConfig
                {
                    ConnectionString = connectionString,
                    DatabaseName = databaseName
                };
            }
            else
            {
                // if the mongo config is found but some values are missing, use defaults
                if (string.IsNullOrEmpty(mongoConfig.ConnectionString))
                {
                    mongoConfig.ConnectionString = _configuration.GetSection("MongoDB:ConnectionString").Value
                        ?? throw new InvalidOperationException("MongoDB connection string not found");
                }

                if (string.IsNullOrEmpty(mongoConfig.DatabaseName))
                {
                    mongoConfig.DatabaseName = _tenantContext.TenantId;
                }
            }

            return mongoConfig;
        }
    }
} 