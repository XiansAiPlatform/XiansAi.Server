using System.Net.Http.Json;
using XiansAi.Server.Tests.TestUtils;
using Microsoft.AspNetCore.Mvc.Testing;
using Features.AgentApi.Endpoints;

namespace XiansAi.Server.Tests.IntegrationTests;

public class AuthenticationTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    private readonly HttpClient _clientWithoutCert;
    
    public AuthenticationTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
        // Create a client without certificate to test unauthorized access
        _clientWithoutCert = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        // Remove any existing certificate headers to ensure it's truly unauthenticated
        if (_clientWithoutCert.DefaultRequestHeaders.Contains("X-Client-Cert"))
        {
            _clientWithoutCert.DefaultRequestHeaders.Remove("X-Client-Cert");
        }
    }
    
    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AuthenticationTests.AccessProtectedEndpoint_WithValidCertificate_ReturnsSuccess"
    */
    [Fact]
    public async Task AccessProtectedEndpoint_WithValidCertificate_ReturnsSuccess()
    {
        // Arrange - This client already has the certificate from the base class
        var request = new CacheKeyRequest { Key = "any-key" };
        
        // Act - Access endpoint that requires certificate auth
        var response = await _client.PostAsJsonAsync("/api/client/cache/get", request);
        
        // Assert - Should be not found (404) rather than unauthorized (401),
        // which means the auth succeeded but the key wasn't found
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
    }
    
    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AuthenticationTests.AccessProtectedEndpoint_WithoutCertificate_ReturnsUnauthorized"
    */
    [Fact]
    public async Task AccessProtectedEndpoint_WithoutCertificate_ReturnsUnauthorized()
    {
        // Arrange - Use client without certificate
        var request = new CacheKeyRequest { Key = "any-key" };
        
        // Act - Access endpoint that requires certificate auth
        var response = await _clientWithoutCert.PostAsJsonAsync("/api/client/cache/get", request);
        
        // Assert - Should be unauthorized
        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
} 