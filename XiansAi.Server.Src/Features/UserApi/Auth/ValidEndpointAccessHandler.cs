using Shared.Auth;

namespace Features.UserApi.Auth
{
    public class ValidEndpointAccessHandler : UserApiTenantContextHandler<ValidEndpointAccessRequirement>
    {
        public ValidEndpointAccessHandler(
            ITenantContext tenantContext,
            ILogger<ValidEndpointAccessHandler> logger)
            : base(tenantContext, logger)
        {
        }
    }
}
