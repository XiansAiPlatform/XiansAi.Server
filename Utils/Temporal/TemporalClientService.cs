using System.Text.Json;
using Temporalio.Client;
using System.Collections.Concurrent;
using XiansAi.Server.Auth;
using Temporalio.Api.Cloud.CloudService.V1;
using Temporalio.Api.Cloud.Namespace.V1;
using Temporalio.Api.Cloud.Identity.V1;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace XiansAi.Server.Temporal;

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
    private readonly IKeyVaultService _keyVaultService;
    private readonly ILogger<TemporalClientService> _logger;
    private readonly ITenantContext _tenantContext;

    public TemporalClientService(
    IKeyVaultService keyVaultService,
    ILogger<TemporalClientService> logger,
    ITenantContext tenantContext)
    {
        _keyVaultService = keyVaultService ?? throw new ArgumentNullException(nameof(keyVaultService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public ITemporalClient GetClient()
    {
        var config = _tenantContext.GetTemporalConfig();
        
        if (config.FlowServerUrl != "localhost:7233")
        {
            // create clients for each tenant
            return _clients.GetOrAdd(_tenantContext.TenantId, _ =>
            {

                // if tls is true, use the tls options. Otherwise, use the api key
                TemporalClientConnectOptions options =
                    new TemporalClientConnectOptions(new(config.FlowServerUrl))
                    {
                        Namespace = config.FlowServerNamespace!,
                        Tls = new TlsOptions()
                        {
                            ClientCert = GetCertificate().GetAwaiter().GetResult(),
                            ClientPrivateKey = GetPrivateKey().GetAwaiter().GetResult(),
                        }
                    };

                _logger.LogInformation("Connecting to temporal server---" + config.FlowServerUrl + "---, namespace---" + config.FlowServerNamespace + "---");

                return TemporalClient.ConnectAsync(options).GetAwaiter().GetResult();
            });
        }
        else
        {
            // create a local client
            return _clients.GetOrAdd("local", _ =>
            {
                var config = _tenantContext.GetTemporalConfig();
                _logger.LogInformation("Connecting to local temporal - " + config.FlowServerUrl + "---, namespace---" + config.FlowServerNamespace + "---");
                var options = new TemporalClientConnectOptions(new(config.FlowServerUrl)) // Local Temporal URL
                {
                    Namespace = "default", // Default local namespace
                };

                return TemporalClient.ConnectAsync(options).GetAwaiter().GetResult();
            });
        }

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
                OwnerType = "user",
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
            Permission = "admin"
        });
        access.AccountAccess = new AccountAccess
        {
            Role = "admin"
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

    private async Task<byte[]> GetCertificate()
    {
        var config = _tenantContext.GetTemporalConfig();
        if (config.CertificateFilePath != null)
        {
            _logger.LogInformation("Loading certificate from file---" + config.CertificateFilePath + "---");
            return await File.ReadAllBytesAsync(config.CertificateFilePath);
        }
        else if (config.CertificateKeyVaultName != null)
        {
            _logger.LogInformation("Loading certificate from key vault---" + config.CertificateKeyVaultName + "---");
            var certificateString = await _keyVaultService.LoadSecret(config.CertificateKeyVaultName);
            return Convert.FromBase64String(certificateString);
        }
        else
        {
            throw new Exception("Certificate is not set in the configuration");
        }
    }

    private async Task<byte[]> GetPrivateKey()
    {
        var config = _tenantContext.GetTemporalConfig();
        if (config.PrivateKeyFilePath != null)
            return await File.ReadAllBytesAsync(config.PrivateKeyFilePath);
        else if (config.PrivateKeyKeyVaultName != null)
        {
            _logger.LogInformation("Loading private key from key vault---" + config.PrivateKeyKeyVaultName + "---");

            var privateKeyString = await _keyVaultService.LoadSecret(config.PrivateKeyKeyVaultName);

            try
            {
                return Convert.FromBase64String(privateKeyString);
            }
            catch (FormatException ex)
            {
                _logger.LogError($"Failed to decode private key: {ex.Message}");
                throw new FormatException("The private key retrieved from key vault is not in valid Base64 format", ex);
            }
        }
        else
        {
            throw new Exception("Private key is not set in the configuration");
        }
    }
}