using Shared.Auth;

namespace Features.UserApi.Auth
{
    public class ValidWebsocketAccessHandler : UserApiTenantContextHandler<ValidWebsocketAccessRequirement>
    {
        public ValidWebsocketAccessHandler(
            ITenantContext tenantContext,
            ILogger<ValidWebsocketAccessHandler> logger)
            : base(tenantContext, logger)
        {
        }
    }
}
