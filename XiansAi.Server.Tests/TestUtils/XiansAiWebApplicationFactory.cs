using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Moq;
using Shared.Data;
using Shared.Services;
using Microsoft.Extensions.Logging;
using System.IO;
using Shared.Utils.Services;
using Shared.Providers.Auth;
using Shared.Utils;
using Shared.Data.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;

namespace Tests.TestUtils;

public class XiansAiWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly MongoDbFixture _mongoFixture;
    private const string TestTenantId = "test-tenant";
    private readonly string? _environment;

    public XiansAiWebApplicationFactory(MongoDbFixture mongoFixture, string? environment = null)
    {
        _mongoFixture = mongoFixture;
        _environment = environment;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment ?? "Tests");

        // Set up test configuration
        builder.ConfigureAppConfiguration((context, config) =>
        {
            config.Sources.Clear();
            
            // Add the test configuration file
            var testConfigPath = Path.Combine(Directory.GetCurrentDirectory(), "XiansAi.Server.Tests", "appsettings.Tests.json");
            if (File.Exists(testConfigPath))
            {
                config.AddJsonFile(testConfigPath, optional: false, reloadOnChange: false);
            }

            // Override MongoDB connection string
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MongoDB:ConnectionString"] = _mongoFixture.MongoConfig.ConnectionString,
                ["MongoDB:DatabaseName"] = _mongoFixture.MongoConfig.DatabaseName
            });
        });

        builder.ConfigureServices(services =>
        {
            // Override MongoDB services
            RemoveService<IMongoDbClientService>(services);
            services.AddSingleton<IMongoDbClientService>(_mongoFixture.MongoClientService);

            // Mock external services only
            var mockEmailService = new Mock<IEmailService>();
            mockEmailService.Setup(x => x.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Returns(Task.CompletedTask);
            RemoveService<IEmailService>(services);
            services.AddSingleton(mockEmailService.Object);

            var mockLlmService = new Mock<ILlmService>();
            mockLlmService.Setup(x => x.GetApiKey())
                .Returns("test-api-key");
            mockLlmService.Setup(x => x.GetLlmProvider())
                .Returns("test-provider");
            RemoveService<ILlmService>(services);
            services.AddSingleton(mockLlmService.Object);

            // Mock background task service
            var mockBackgroundTaskService = new Mock<IBackgroundTaskService>();
            RemoveService<IBackgroundTaskService>(services);
            services.AddSingleton(mockBackgroundTaskService.Object);

            // Mock IUserTenantService to always return the test tenant for the test user
            var mockUserTenantService = new Mock<IUserTenantService>();
            // Authorize the test user for both tenants
            var authorizedTenants = new List<string> { TestTenantId, "99x.io" };
            mockUserTenantService.Setup(x => x.GetTenantsForCurrentUser())
                .ReturnsAsync(ServiceResult<List<string>>.Success(authorizedTenants));
            RemoveService<IUserTenantService>(services);
            services.AddSingleton<IUserTenantService>(mockUserTenantService.Object);

            // Mock CertificateGenerator to avoid certificate configuration issues
            RemoveService<CertificateGenerator>(services);
            services.AddSingleton<CertificateGenerator>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<CertificateGenerator>>();
                var env = sp.GetRequiredService<IWebHostEnvironment>();
                
                // Create a test configuration with dummy certificate data
                var testConfig = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Certificates:AppServerPfxBase64"] = "MIIJgQIBAzCCCUcGCSqGSIb3DQEHAaCCCTgEggk0MIIJMDCCBWcGCSqGSIb3DQEHBqCCBVgwggVUAgEAMIIFTQYJKoZIhvcNAQcBMBwGCiqGSIb3DQEMAQYwDgQI", // Dummy base64
                        ["Certificates:AppServerCertPassword"] = "test-password"
                    })
                    .Build();
                    
                return new CertificateGenerator(testConfig, logger, env);
            });

            // Mock ITenantContext to provide test context
            var mockTenantContext = new Mock<ITenantContext>();
            mockTenantContext.SetupProperty<string>(x => x.TenantId, TestTenantId);
            mockTenantContext.SetupProperty<string>(x => x.LoggedInUser, "test-user");
            mockTenantContext.SetupProperty<IEnumerable<string>>(x => x.AuthorizedTenantIds, new List<string> { TestTenantId, "99x.io" });
            mockTenantContext.SetupProperty<string[]>(x => x.UserRoles, new[] { "SysAdmin", "TenantAdmin", "TenantUser" });
            RemoveService<ITenantContext>(services);
            services.AddSingleton(mockTenantContext.Object);

            // Seed the test tenant in the database if it does not exist
            var mongoClient = _mongoFixture.MongoClientService.GetClient();
            var database = mongoClient.GetDatabase(_mongoFixture.MongoConfig.DatabaseName);
            var tenantsCollection = database.GetCollection<Tenant>("tenants");
            var tenantsToSeed = new[] { TestTenantId, "99x.io" };
            foreach (var tenantId in tenantsToSeed)
            {
                var filter = Builders<Tenant>.Filter.Eq(t => t.TenantId, tenantId);
                var existingTenant = tenantsCollection.FindSync(filter).FirstOrDefault();
                if (existingTenant == null)
                {
                    tenantsCollection.InsertOne(new Tenant
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        TenantId = tenantId,
                        Name = tenantId == TestTenantId ? "Test Tenant" : "99x.io Tenant",
                        Domain = tenantId == TestTenantId ? "test-tenant.example.com" : "99x.io",
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = "test-user",
                        UpdatedAt = DateTime.UtcNow
                    });
                }
            }

            // Configure logging to reduce noise in tests
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));

            // Register test authentication handler as default
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = "Test";
                options.DefaultChallengeScheme = "Test";
                options.DefaultScheme = "Test";
            })
            .AddScheme<TestAuthenticationOptions, TestAuthHandler>("Test", options => { });

            // Override AgentApi certificate authentication policy to use test authentication
            services.AddAuthorization(options =>
            {
                // Override AgentApi policy
                options.AddPolicy("RequireCertificate", policy =>
                {
                    policy.AuthenticationSchemes.Clear();
                    policy.AuthenticationSchemes.Add("Test"); // Use test auth instead of certificate auth
                    policy.RequireAuthenticatedUser();
                });

                // Override WebApi policies to use test authentication instead of JWT
                options.AddPolicy("RequireTokenAuth", policy =>
                {
                    policy.AuthenticationSchemes.Clear();
                    policy.AuthenticationSchemes.Add("Test");
                    policy.RequireAuthenticatedUser();
                });
                
                options.AddPolicy("RequireTenantAuth", policy =>
                {
                    policy.AuthenticationSchemes.Clear(); 
                    policy.AuthenticationSchemes.Add("Test");
                    policy.RequireAuthenticatedUser();
                });
                
                options.AddPolicy("RequireTenantAuthWithoutConfig", policy =>
                {
                    policy.AuthenticationSchemes.Clear();
                    policy.AuthenticationSchemes.Add("Test");
                    policy.RequireAuthenticatedUser();
                });

                options.AddPolicy("RequireSysAdmin", policy =>
                {
                    policy.AuthenticationSchemes.Clear();
                    policy.AuthenticationSchemes.Add("Test");
                    policy.RequireAuthenticatedUser();
                });

                options.AddPolicy("RequireTenantAdmin", policy =>
                {
                    policy.AuthenticationSchemes.Clear();
                    policy.AuthenticationSchemes.Add("Test");
                    policy.RequireAuthenticatedUser();
                });
            });

            // Mock IAuthProvider and IAuthProviderFactory for token validation
            var mockAuthProvider = new Mock<IAuthProvider>();
            mockAuthProvider.Setup(x => x.ValidateToken(It.IsAny<string>())).ReturnsAsync((true, "test-user"));
            services.AddSingleton<IAuthProvider>(mockAuthProvider.Object);

            var mockAuthProviderFactory = new Mock<IAuthProviderFactory>();
            mockAuthProviderFactory.Setup(x => x.GetProvider()).Returns(mockAuthProvider.Object);
            services.AddSingleton<IAuthProviderFactory>(mockAuthProviderFactory.Object);
        });
    }

    private static void RemoveService<T>(IServiceCollection services)
    {
        var descriptors = services.Where(d => d.ServiceType == typeof(T)).ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    public string GetTestTenantId() => TestTenantId;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // MongoDbFixture will be disposed by the test class
        }
        base.Dispose(disposing);
    }
} 