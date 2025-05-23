using Temporalio.Client;
using System.Collections.Concurrent;
using Temporalio.Api.Cloud.CloudService.V1;
using Temporalio.Api.Cloud.Namespace.V1;
using Temporalio.Api.Cloud.Identity.V1;
using Google.Protobuf.WellKnownTypes;
using Shared.Auth;

namespace Shared.Utils.Temporal;

public interface ITemporalClientService
{
    ITemporalClient GetClient();

    Task<string> CreateNamespaceAsync(string name);

    Task<string> CreateServiceAccountAsync(string name);

    Task<string> CreateApiKeyAsync(string name);

}

public class TemporalClientService : ITemporalClientService
{
    private static readonly ConcurrentDictionary<string, ITemporalClient> _clients = new();
    private static readonly ConcurrentDictionary<string, CloudService> _serviceClients = new();
    private readonly ILogger<TemporalClientService> _logger;
    private readonly ITenantContext _tenantContext;

    public TemporalClientService(
    ILogger<TemporalClientService> logger,
    ITenantContext tenantContext)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public ITemporalClient GetClient()
    {
        // create clients for each tenant
        return _clients.GetOrAdd(_tenantContext.TenantId, _ =>
        {
            var config = _tenantContext.GetTemporalConfig();

            // if tls is true, use the tls options. Otherwise, use the api key
            TemporalClientConnectOptions options =
                new TemporalClientConnectOptions(new(config.FlowServerUrl))
                {
                    Namespace = config.FlowServerNamespace!,
                    Tls = new TlsOptions()
                    {
                        ClientCert = GetCertificate(),
                        ClientPrivateKey = GetPrivateKey(),
                    }
                };            
            
            _logger.LogInformation("Connecting to temporal server---" + config.FlowServerUrl + "---, namespace---" + config.FlowServerNamespace + "---");

            return TemporalClient.ConnectAsync(options).GetAwaiter().GetResult();
        });
    }

    public async Task<string[]> GetAvailableRegions()
    {
        var client = await GetCloudService();
        var request = new GetRegionsRequest();
        var response = await client.GetRegionsAsync(request);
        return response.Regions.Select(r => r.Id).ToArray();
    }

    public async Task<string> CreateApiKeyAsync(string apiKeyName)
    {
        var client = await GetCloudService();
        var request = new CreateApiKeyRequest
        {
            Spec = new ApiKeySpec
            {
                DisplayName = "XiansAi API Key",
                Description = "API key used for XiansAi to work with this namespace",
                OwnerId = "8b40c2ac66664fce8c6e958ec0f884bf",
                OwnerType = OwnerType.User,
                ExpiryTime = Timestamp.FromDateTime(DateTime.UtcNow.AddDays(90)),
            }
        };
        var response = await client.CreateApiKeyAsync(request);
        return response.Token;
    }



    public async Task<string> CreateServiceAccountAsync(string name)
    {
        var client = await GetCloudService();
        var access = new Access();
        access.NamespaceAccesses.Add(name.ToLower(), new NamespaceAccess
        {
            Permission = NamespaceAccess.Types.Permission.Admin
        });
        access.AccountAccess = new AccountAccess
        {
            Role = AccountAccess.Types.Role.Admin
        };

        var request = new CreateServiceAccountRequest
        {
            Spec = new ServiceAccountSpec
            {
                Name = name.ToLower(),
                Access = access,
                Description = "Service account for XiansAi to work with this namespace"
            }
        };

        var response = await client.CreateServiceAccountAsync(request);
        return response.ServiceAccountId;
    }

    public async Task<string> CreateNamespaceAsync(string namespaceName)
    {
        var client = await GetCloudService();
        var request = new CreateNamespaceRequest
        {
            Spec = new NamespaceSpec
            {
                // temporal namespace names can only be in lowercases
                Name = namespaceName.ToLower(),
                RetentionDays = 30,
                ApiKeyAuth = new ApiKeyAuthSpec
                {
                    Enabled = true,
                },
                MtlsAuth = new MtlsAuthSpec
                {
                    Enabled = false,
                },
                Regions = { "aws-eu-west-1" },

            },

        };

        var response = await client.CreateNamespaceAsync(request);
        return response.Namespace;
    }

    public async Task<CloudService> GetCloudService()
    {
        var tenantId = _tenantContext.TenantId;
        var apiKey = "eyJhbGciOiJFUzI1NiIsImtpZCI6Ild2dHdhQSJ9.eyJhY2NvdW50X2lkIjoidHN1YWgiLCJhdWQiOlsidGVtcG9yYWwuaW8iXSwiZXhwIjoxNzQ0NTIwNTYyLCJpc3MiOiJ0ZW1wb3JhbC5pbyIsImp0aSI6ImFBQm9rZ05XeE9rMlUxUnNIbnQ1V2VHT0h0eFlSZHJXIiwia2V5X2lkIjoiYUFCb2tnTld4T2syVTFSc0hudDVXZUdPSHR4WVJkclciLCJzdWIiOiI2ZTMxOTgxYzA2OTk0MDkwYTc1N2MxYzJkNzMyMGI4MyJ9.KrEpMh8UOZJFOuEHv_EKgKu1-kXsVRm2L-M0NDLg2aGDLyvYHgceDzYhD1tKmnnOaGohAUlVNt_APEGC5oprnQ";
        var options = new TemporalCloudOperationsClientConnectOptions(apiKey);
        options.Version = "2024-10-01-00";
        var client = await TemporalCloudOperationsClient.ConnectAsync(options);

        if (tenantId == null)
        {
            // new tenant registration
            return client.CloudService;
        }
        else
        {
            // existing tenant
            return _serviceClients.GetOrAdd(tenantId, _ =>
            {
                return client.CloudService;
            });
        }
    }

    private byte[] GetCertificate()
    {
        var config = _tenantContext.GetTemporalConfig();
        return Convert.FromBase64String(config.CertificateBase64 ?? throw new Exception("Certificate is not set in the configuration"));
    }

    private byte[] GetPrivateKey()
    {
        var config = _tenantContext.GetTemporalConfig();
        return Convert.FromBase64String(config.PrivateKeyBase64 ?? throw new Exception("Private key is not set in the configuration"));
    }
}