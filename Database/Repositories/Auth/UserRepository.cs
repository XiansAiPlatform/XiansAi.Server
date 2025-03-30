using MongoDB.Driver;
using System.Threading.Tasks;
using XiansAi.Server.Auth.Models;
using XiansAi.Server.Database;

namespace XiansAi.Server.Auth.Repositories
{
    public class UserRepository : IUserRepository
    {
        private readonly IDatabaseService _databaseService;
        private readonly ITenantContext _tenantContext;

        public UserRepository(IDatabaseService databaseService, ITenantContext tenantContext)
        {
            _databaseService = databaseService;
            _tenantContext = tenantContext;
        }

        public async Task<UserDocument?> GetUserByIdAsync(string userId)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<UserDocument>("users");
            return await collection.Find(u => u.Id == userId).FirstOrDefaultAsync();
        }

        public async Task<UserDocument?> GetUserByAuth0IdAsync(string auth0Id)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<UserDocument>("users");
            return await collection.Find(u => u.Auth0Id == auth0Id).FirstOrDefaultAsync();
        }

        public async Task<UserDocument?> GetUserByEmailAsync(string email)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<UserDocument>("users");
            return await collection.Find(u => u.Email == email).FirstOrDefaultAsync();
        }

        public async Task<UserDocument> CreateUserAsync(UserDocument user)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<UserDocument>("users");
            await collection.InsertOneAsync(user);
            return user;
        }

        public async Task<bool> UpdateUserAsync(UserDocument user)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<UserDocument>("users");
            var result = await collection.ReplaceOneAsync(u => u.Id == user.Id, user);
            return result.ModifiedCount > 0;
        }

        public async Task<bool> DeleteUserAsync(string userId)
        {
            var database = await _databaseService.GetDatabase();
            var collection = database.GetCollection<UserDocument>("users");
            var result = await collection.DeleteOneAsync(u => u.Id == userId);
            return result.DeletedCount > 0;
        }
    }
} 