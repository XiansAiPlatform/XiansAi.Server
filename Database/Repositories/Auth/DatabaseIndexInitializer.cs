using MongoDB.Driver;
using System.Threading.Tasks;
using XiansAi.Server.Auth.Models;
using XiansAi.Server.Database;

namespace XiansAi.Server.Auth.Repositories
{
    public class DatabaseIndexInitializer
    {
        private readonly IDatabaseService _databaseService;

        public DatabaseIndexInitializer(IDatabaseService databaseService)
        {
            _databaseService = databaseService;
        }

        public async Task InitializeIndexesAsync()
        {
            var database = await _databaseService.GetDatabase();
            
            // User indexes
            var users = database.GetCollection<UserDocument>("users");
            await users.Indexes.CreateOneAsync(
                new CreateIndexModel<UserDocument>(
                    Builders<UserDocument>.IndexKeys.Ascending(u => u.Auth0Id)));

            await users.Indexes.CreateOneAsync(
                new CreateIndexModel<UserDocument>(
                    Builders<UserDocument>.IndexKeys.Ascending(u => u.Email)));

            await users.Indexes.CreateOneAsync(
                new CreateIndexModel<UserDocument>(
                    Builders<UserDocument>.IndexKeys.Ascending("TenantMemberships.TenantId")));

            // Permission indexes
            var permissions = database.GetCollection<PermissionDocument>("permissions");
            await permissions.Indexes.CreateOneAsync(
                new CreateIndexModel<PermissionDocument>(
                    Builders<PermissionDocument>.IndexKeys
                        .Ascending(p => p.EntityId)
                        .Ascending(p => p.EntityType)
                        .Ascending(p => p.TenantId)));

            await permissions.Indexes.CreateOneAsync(
                new CreateIndexModel<PermissionDocument>(
                    Builders<PermissionDocument>.IndexKeys
                        .Ascending("Permissions.UserId")
                        .Ascending(p => p.TenantId)));
        }
    }
} 