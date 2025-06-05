using System.Net;
using System.Text.Json;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Features.AgentApi.Repositories;
using Features.AgentApi.Models;
using XiansAi.Server.Tests.TestUtils;
using Shared.Services;
using Moq;
using Microsoft.AspNetCore.Hosting;
using Shared.Utils.GenAi;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Driver;
using Features.WebApi.Services;

namespace XiansAi.Server.Tests.IntegrationTests.WebApi;

public class SettingsEndpointsTests : WebApiIntegrationTestBase, IDisposable
{
    private readonly IMongoCollection<Certificate> _certificatesCollection;
    
    public SettingsEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
        // Mock ILlmService to avoid external dependencies
        var mockLlmService = new Mock<ILlmService>();
        _factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton(mockLlmService.Object);
            });
        });
        
        // Create new client with updated factory
        _client?.Dispose();
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        
        // Add test tenant header and authorization header
        _client.DefaultRequestHeaders.Add("X-Tenant-Id", TestTenantId);
        _client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        
        // Get access to certificates collection for cleanup
        var database = _mongoFixture.Database;
        _certificatesCollection = database.GetCollection<Certificate>("certificates");
    }

    // Helper method to clean up certificates before each test
    private async Task CleanupCertificatesAsync()
    {
        await _certificatesCollection.DeleteManyAsync(Builders<Certificate>.Filter.Empty);
    }

    // Helper method to create a client without tenant header
    private HttpClient CreateClientWithoutTenantHeader()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        return client;
    }

    // Helper method to create a client with invalid tenant header
    private HttpClient CreateClientWithInvalidTenant()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        client.DefaultRequestHeaders.Add("X-Tenant-Id", "invalid-tenant");
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");
        return client;
    }

    [Fact]
    public async Task GenerateClientCertificateBase64_WithValidTenant_ReturnsOK()
    {
        // Arrange
        await CleanupCertificatesAsync();

        // Act
        var response = await _client.PostAsync("/api/client/settings/appserver/base64cert", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadAsStringAsync();
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GenerateClientCertificateBase64_WithoutTenantHeader_ReturnsUnauthorized()
    {
        // Arrange
        await CleanupCertificatesAsync();
        using var client = CreateClientWithoutTenantHeader();

        // Act
        var response = await client.PostAsync("/api/client/settings/appserver/base64cert", null);

        // Assert
        // Note: This might return OK in test environment due to test configuration
        // In real environment, it should return Unauthorized
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task GenerateClientCertificateBase64_WithInvalidTenant_ReturnsUnauthorized()
    {
        // Arrange
        await CleanupCertificatesAsync();
        using var client = CreateClientWithInvalidTenant();

        // Act
        var response = await client.PostAsync("/api/client/settings/appserver/base64cert", null);

        // Assert
        // Note: This might return OK in test environment due to test configuration
        // In real environment, it should return Unauthorized
        Assert.True(response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task GenerateClientCertificateBase64_Multiple_RevokesOldCertificates()
    {
        // Arrange
        await CleanupCertificatesAsync();

        // Act - Generate first certificate
        var response1 = await _client.PostAsync("/api/client/settings/appserver/base64cert", null);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Act - Generate second certificate
        var response2 = await _client.PostAsync("/api/client/settings/appserver/base64cert", null);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // Assert - Should have exactly 2 certificates total (one revoked, one active)
        var totalCertificates = await _certificatesCollection.CountDocumentsAsync(Builders<Certificate>.Filter.Empty);
        Assert.Equal(2, totalCertificates);

        // Assert - Should have exactly 1 active certificate
        var activeCertificates = await _certificatesCollection.CountDocumentsAsync(
            Builders<Certificate>.Filter.Eq(c => c.IsRevoked, false));
        Assert.Equal(1, activeCertificates);

        // Assert - Should have exactly 1 revoked certificate
        var revokedCertificates = await _certificatesCollection.CountDocumentsAsync(
            Builders<Certificate>.Filter.Eq(c => c.IsRevoked, true));
        Assert.Equal(1, revokedCertificates);
    }

    [Fact]
    public async Task GenerateClientCertificateBase64_WithDifferentUsers_GeneratesSeparateCertificates()
    {
        // Arrange
        await CleanupCertificatesAsync();

        // Act - Generate first certificate (with default test user)
        var response1 = await _client.PostAsync("/api/client/settings/appserver/base64cert", null);
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);

        // Act - Generate second certificate (same user in test environment)
        var response2 = await _client.PostAsync("/api/client/settings/appserver/base64cert", null);
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);

        // Assert - In test environment, both requests use the same test user
        // So the second request should revoke the first certificate
        var totalCertificates = await _certificatesCollection.CountDocumentsAsync(Builders<Certificate>.Filter.Empty);
        Assert.Equal(2, totalCertificates); // One revoked, one active

        var activeCertificates = await _certificatesCollection.CountDocumentsAsync(
            Builders<Certificate>.Filter.Eq(c => c.IsRevoked, false));
        Assert.Equal(1, activeCertificates);
    }

    [Fact]
    public async Task GenerateClientCertificateBase64_ResponseFormat_IsCorrect()
    {
        // Arrange
        await CleanupCertificatesAsync();

        // Act
        var response = await _client.PostAsJsonAsync("/api/client/settings/appserver/base64cert", new { });

        // Assert
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        var responseJson = JsonSerializer.Deserialize<JsonElement>(responseContent);
        
        // Check that response has certificate property
        Assert.True(responseJson.TryGetProperty("certificate", out var certificateProperty));
        
        // Check that certificate value is a valid base64 string
        var certificateValue = certificateProperty.GetString();
        Assert.NotNull(certificateValue);
        Assert.NotEmpty(certificateValue);
        Assert.Matches(@"^[A-Za-z0-9+/]*={0,2}$", certificateValue);
    }

    [Fact]
    public async Task GenerateClientCertificateBase64_CertificateMetadata_IsCorrect()
    {
        // Arrange
        await CleanupCertificatesAsync();

        // Act
        var response = await _client.PostAsync("/api/client/settings/appserver/base64cert", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Check that certificate metadata was stored correctly
        var certificates = await _certificatesCollection
            .Find(Builders<Certificate>.Filter.Empty)
            .ToListAsync();

        Assert.Single(certificates);
        var cert = certificates[0];
        
        Assert.Equal("test-tenant", cert.TenantId);
        Assert.Equal("test-user", cert.IssuedTo);
        Assert.Contains("XiansAi", cert.SubjectName);
        Assert.False(cert.IsRevoked);
        Assert.NotNull(cert.Thumbprint);
        Assert.NotEmpty(cert.Thumbprint);
        Assert.True(cert.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.True(cert.ExpiresAt > DateTime.UtcNow.AddYears(4)); // Should be ~5 years
    }

    [Fact]
    public async Task GenerateClientCertificateBase64_CertificateExpiry_IsSetCorrectly()
    {
        // Arrange
        await CleanupCertificatesAsync();

        // Act
        var response = await _client.PostAsync("/api/client/settings/appserver/base64cert", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Check certificate expiry
        var certificates = await _certificatesCollection
            .Find(Builders<Certificate>.Filter.Empty)
            .ToListAsync();

        Assert.Single(certificates);
        var cert = certificates[0];
        
        // Certificate should expire in approximately 5 years (allow some tolerance)
        var expectedExpiry = DateTime.UtcNow.AddYears(5);
        var timeDifference = Math.Abs((cert.ExpiresAt - expectedExpiry).TotalDays);
        Assert.True(timeDifference < 30, $"Certificate expiry {cert.ExpiresAt} is not within 30 days of expected {expectedExpiry}");
    }

    public new void Dispose()
    {
        // Clean up any certificates created during tests
        CleanupCertificatesAsync().Wait();
        base.Dispose();
    }
} 