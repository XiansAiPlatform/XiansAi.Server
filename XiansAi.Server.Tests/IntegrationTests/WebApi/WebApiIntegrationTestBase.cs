using Xunit;
using XiansAi.Server.Tests.TestUtils;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Features.WebApi.Auth;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace XiansAi.Server.Tests.IntegrationTests.WebApi;

public abstract class WebApiIntegrationTestBase : IClassFixture<MongoDbFixture>
{
    protected readonly MongoDbFixture _mongoFixture;
    protected WebApplicationFactory<Program> _factory;
    protected HttpClient _client;
    protected const string TestTenantId = "test-tenant";

    protected WebApiIntegrationTestBase(MongoDbFixture mongoFixture)
    {
        _mongoFixture = mongoFixture;
        
        _factory = new XiansAiWebApplicationFactory(mongoFixture)
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((context, config) =>
                {
                    // Add test tenant configuration
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        [$"Tenants:{TestTenantId}:Name"] = "Test Tenant",
                        [$"Tenants:{TestTenantId}:Domain"] = "test.example.com",
                        [$"Tenants:{TestTenantId}:Temporal:CertificateBase64"] = "test-cert",
                        [$"Tenants:{TestTenantId}:Temporal:PrivateKeyBase64"] = "test-key",
                        [$"Tenants:{TestTenantId}:Temporal:FlowServerUrl"] = "http://localhost:7233"
                    });
                });
                
                builder.ConfigureServices(services =>
                {
                    // Override authorization policies to use Test authentication scheme
                    services.AddAuthorization(options =>
                    {
                        // Clear existing policies
                        options.DefaultPolicy = new AuthorizationPolicyBuilder("Test")
                            .RequireAuthenticatedUser()
                            .Build();

                        // Override WebApi policies to use Test scheme
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
                    });
                });
            });
        
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        // Add test tenant header
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", TestTenantId);
    }

    protected async Task<HttpResponseMessage> GetAsync(string requestUri)
    {
        return await _client.GetAsync(requestUri);
    }

    protected async Task<HttpResponseMessage> PostAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.PostAsync(requestUri, content);
    }

    protected async Task<HttpResponseMessage> PutAsJsonAsync<T>(string requestUri, T value)
    {
        var json = JsonSerializer.Serialize(value);
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        return await _client.PutAsync(requestUri, content);
    }

    protected async Task<HttpResponseMessage> DeleteAsync(string requestUri)
    {
        return await _client.DeleteAsync(requestUri);
    }

    protected async Task<T?> ReadAsJsonAsync<T>(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
    }

    public void Dispose()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }
} 