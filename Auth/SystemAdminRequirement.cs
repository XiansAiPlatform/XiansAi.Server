using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using XiansAi.Server.Auth.Repositories;

namespace XiansAi.Server.Auth
{
    public class SystemAdminRequirement : BaseAuthRequirement
    {
        public SystemAdminRequirement(IConfiguration configuration) : base(configuration) { }
    }

    public class SystemAdminHandler : BaseAuthHandler<SystemAdminRequirement>
    {
        private readonly IUserRepository _userRepository;

        public SystemAdminHandler(
            ILogger<SystemAdminHandler> logger,
            ITenantContext tenantContext,
            IUserRepository userRepository) 
            : base(logger, tenantContext)
        {
            _userRepository = userRepository;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            SystemAdminRequirement requirement)
        {
            var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);

            if (!success || string.IsNullOrEmpty(loggedInUser))
            {
                return;
            }

            var user = await _userRepository.GetUserByAuth0IdAsync(loggedInUser);
            
            if (user != null && user.IsSystemAdmin)
            {
                context.Succeed(requirement);
            }
        }
    }
} 