using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.EndpointExt.WebClient;

namespace XiansAi.Server.EndpointExt;
public static class WebAdminEndpointExtensions
{
    public static void MapAdminEndpoints(this WebApplication app)
    {
        MapTenantEndpoints(app);
    }

    private static void MapTenantEndpoints(this WebApplication app)
    {
     
    }
}