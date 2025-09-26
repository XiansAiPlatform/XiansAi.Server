using System.Net;
using System.Text.Json;
using Xunit;
using Features.WebApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Utils.Services;
using Shared.Services;
using Moq;
using Tests.TestUtils;
using Shared.Utils;
using System.Text;
using Shared.Providers.Auth.Auth0;
using Microsoft.AspNetCore.Hosting;
using Shared.Auth;

namespace Tests.IntegrationTests.WebApi;

public class PublicEndpointsTests : WebApiIntegrationTestBase
{
    private const string TestEmail = "test@99x.io";
    private const string ValidCode = "123456";

    public PublicEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task SendVerificationCode_WithValidEmail_ReturnsOK()
    {
        // Arrange - Send the email as a JSON string
        var jsonContent = new StringContent(JsonSerializer.Serialize(TestEmail), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/public/register/verification/send", jsonContent);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<SendVerificationCodeResult>(response);
        Assert.NotNull(result);
        Assert.Contains("Verification code sent", result.Message);
        Assert.Contains(TestEmail, result.Message);
    }

    [Fact]
    public async Task SendVerificationCode_WithEmptyEmail_ReturnsBadRequest()
    {
        // Arrange - Send empty string as JSON
        var jsonContent = new StringContent(JsonSerializer.Serialize(""), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/public/register/verification/send", jsonContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var errorMessage = await response.Content.ReadAsStringAsync();
        Assert.Contains("Email is required", errorMessage);
    }

    [Fact]
    public async Task SendVerificationCode_WithNullEmail_ReturnsBadRequest()
    {
        // Arrange - Send null as JSON
        var jsonContent = new StringContent("null", Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/public/register/verification/send", jsonContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendVerificationCode_WithInvalidDomain_ReturnsBadRequest()
    {
        // Arrange
        var jsonContent = new StringContent(JsonSerializer.Serialize("test@invalid-domain.com"), Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/public/register/verification/send", jsonContent);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        
        var errorMessage = await response.Content.ReadAsStringAsync();
        Assert.Contains("This email domain is not registered with Xians.ai", errorMessage);
    }

    private async Task SeedVerificationCode(string email, string code)
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ObjectCache>();
        await cache.SetAsync($"verification:{email}", code, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ValidateCode_WithInvalidCode_ReturnsOK()
    {
        // Arrange - Seed the verification code in cache
        await SeedVerificationCode(TestEmail, ValidCode);
        var request = new ValidateCodeRequest 
        { 
            Email = TestEmail, 
            Code = "invalid-code"
        };

        // Act
        var response = await PostAsJsonAsync("/api/public/register/verification/validate", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<JsonElement>(response);
        Assert.False(result.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task ValidateCode_WithEmptyEmail_ReturnsInternalServerError()
    {
        // Arrange
        var request = new ValidateCodeRequest 
        { 
            Email = "", 
            Code = "123456"
        };

        // Act
        var response = await PostAsJsonAsync("/api/public/register/verification/validate", request);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); // Service throws ArgumentException
    }

    [Fact]
    public async Task ValidateCode_WithEmptyCode_ReturnsInternalServerError()
    {
        // Arrange
        var request = new ValidateCodeRequest 
        { 
            Email = TestEmail, 
            Code = ""
        };

        // Act
        var response = await PostAsJsonAsync("/api/public/register/verification/validate", request);

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); // Service throws ArgumentException
    }

    [Fact]
    public async Task ValidateCode_WithMissingFields_ReturnsBadRequest()
    {
        // Arrange - Send only partial data
        var request = new { Email = TestEmail };

        // Act
        var response = await PostAsJsonAsync("/api/public/register/verification/validate", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ValidateCode_WithNonExistentEmail_ReturnsOK()
    {
        // Arrange - Test with email that never had a verification code sent
        var request = new ValidateCodeRequest 
        { 
            Email = "never-sent@99x.io",
            Code = "123456"
        };

        // Act
        var response = await PostAsJsonAsync("/api/public/register/verification/validate", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<JsonElement>(response);
        Assert.False(result.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task ValidateCode_WithExpiredCode_ReturnsOK()
    {
        // Arrange - Seed the verification code and then remove it to simulate expiration
        await SeedVerificationCode(TestEmail, ValidCode);
        using (var scope = _factory.Services.CreateScope())
        {
        var cache = scope.ServiceProvider.GetRequiredService<ObjectCache>();
        await cache.RemoveAsync($"verification:{TestEmail}");
        }
        var request = new ValidateCodeRequest 
        { 
            Email = TestEmail, 
            Code = "expired-code"
        };

        // Act
        var response = await PostAsJsonAsync("/api/public/register/verification/validate", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<JsonElement>(response);
        Assert.False(result.GetProperty("isValid").GetBoolean());
    }

    private async Task SendVerificationCodeToTestEmail()
    {
        var jsonContent = new StringContent(JsonSerializer.Serialize(TestEmail), Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/api/public/register/verification/send", jsonContent);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string> GetCachedVerificationCode(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ObjectCache>();
        var cacheKey = $"verification:{email}";
        var cachedCode = await cache.GetAsync<string>(cacheKey);
        return cachedCode ?? throw new InvalidOperationException($"No verification code found in cache for {email}");
    }
} 