using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Threading.Tasks;
using XiansAi.Server.Auth.Repositories;

namespace XiansAi.Server.Auth
{
    public class EntityPermissionRequirement : BaseAuthRequirement
    {
        public string RequiredPermissionLevel { get; }

        public EntityPermissionRequirement(
            IConfiguration configuration,
            string requiredPermissionLevel) : base(configuration)
        {
            RequiredPermissionLevel = requiredPermissionLevel;
        }
    }

    public class EntityPermissionHandler : BaseAuthHandler<EntityPermissionRequirement>
    {
        private readonly IUserRepository _userRepository;
        private readonly IPermissionRepository _permissionRepository;

        public EntityPermissionHandler(
            ILogger<EntityPermissionHandler> logger,
            ITenantContext tenantContext,
            IUserRepository userRepository,
            IPermissionRepository permissionRepository) 
            : base(logger, tenantContext)
        {
            _userRepository = userRepository;
            _permissionRepository = permissionRepository;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            EntityPermissionRequirement requirement)
        {
            var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);

            if (!success || string.IsNullOrEmpty(loggedInUser) || !(authorizedTenantIds?.Any() ?? false))
            {
                return;
            }

            // Get the entity from the resource
            if (!(context.Resource is EntityOperationResource resource))
            {
                return;
            }

            var user = await _userRepository.GetUserByAuth0IdAsync(loggedInUser);
            
            if (user == null)
            {
                return;
            }

            // System admins can do anything
            if (user.IsSystemAdmin)
            {
                context.Succeed(requirement);
                return;
            }

            // Check if user is tenant admin for this entity's tenant
            var isTenantAdmin = user.TenantMemberships
                .Any(m => m.TenantId == resource.TenantId && m.Role == "TenantAdmin");
                
            if (isTenantAdmin)
            {
                context.Succeed(requirement);
                return;
            }

            // Check specific permission for this entity
            var permission = await _permissionRepository.GetEntityPermissionAsync(
                resource.EntityId,
                resource.EntityType,
                resource.TenantId);

            if (permission == null)
            {
                return;
            }

            var userPermission = permission.Permissions
                .FirstOrDefault(p => p.UserId == user.Id);

            if (userPermission == null)
            {
                return;
            }

            // Check if the user has the required permission level
            if (HasSufficientPermission(userPermission.Level, requirement.RequiredPermissionLevel))
            {
                context.Succeed(requirement);
            }
        }

        private bool HasSufficientPermission(string actualLevel, string requiredLevel)
        {
            if (actualLevel == "Owner")
            {
                return true;
            }

            if (actualLevel == "Editor" && 
                (requiredLevel == "Editor" || requiredLevel == "Reader"))
            {
                return true;
            }

            if (actualLevel == "Reader" && requiredLevel == "Reader")
            {
                return true;
            }

            return false;
        }
    }
} 