using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using XiansAi.Server.Auth.Repositories;

namespace XiansAi.Server.Auth
{
    public class TenantAdminRequirement : BaseAuthRequirement
    {
        public TenantAdminRequirement(IConfiguration configuration) : base(configuration) { }
    }

    public class TenantAdminHandler : BaseAuthHandler<TenantAdminRequirement>
    {
        private readonly IUserRepository _userRepository;

        public TenantAdminHandler(
            ILogger<TenantAdminHandler> logger,
            ITenantContext tenantContext,
            IUserRepository userRepository) 
            : base(logger, tenantContext)
        {
            _userRepository = userRepository;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            TenantAdminRequirement requirement)
        {
            var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);

            if (!success || string.IsNullOrEmpty(loggedInUser) || !(authorizedTenantIds?.Any() ?? false))
            {
                return;
            }

            var user = await _userRepository.GetUserByAuth0IdAsync(loggedInUser);
            
            if (user == null)
            {
                return;
            }

            // Check if user is system admin
            if (user.IsSystemAdmin)
            {
                context.Succeed(requirement);
                return;
            }

            // Check if user is tenant admin for the current tenant
            var tenantMembership = user.TenantMemberships
                .FirstOrDefault(m => m.TenantId == _tenantContext.TenantId && m.Role == "TenantAdmin");
                
            if (tenantMembership != null)
            {
                context.Succeed(requirement);
            }
        }
    }
} 