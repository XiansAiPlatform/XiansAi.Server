using XiansAi.Server.Auth.Models;
using System.Threading.Tasks;

namespace XiansAi.Server.Auth.Repositories
{
    public interface IUserRepository
    {
        Task<UserDocument?> GetUserByIdAsync(string userId);
        Task<UserDocument?> GetUserByAuth0IdAsync(string auth0Id);
        Task<UserDocument?> GetUserByEmailAsync(string email);
        Task<UserDocument> CreateUserAsync(UserDocument user);
        Task<bool> UpdateUserAsync(UserDocument user);
        Task<bool> DeleteUserAsync(string userId);
    }
} 