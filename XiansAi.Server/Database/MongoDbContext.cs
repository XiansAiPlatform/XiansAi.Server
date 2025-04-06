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

        public MongoDbContext(IConfiguration configuration, ILogger<MongoDbContext> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public MongoDBConfig GetMongoDBConfig()
        {
            var connectionString = _configuration.GetSection("MongoDB:ConnectionString").Value
                ?? throw new InvalidOperationException("MongoDB connection string not found");

            var databaseName = _configuration.GetSection("MongoDB:DatabaseName").Value
                ?? throw new InvalidOperationException("MongoDB database name not found");
            
            // create a new mongo config with default values
            var mongoConfig = new MongoDBConfig
            {
                ConnectionString = connectionString,
                DatabaseName = databaseName
            };

            return mongoConfig;
        }
    }
} 