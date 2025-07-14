using System.Net;
using System.Text.Json;
using Xunit;
using Features.WebApi.Models;
using Microsoft.Extensions.DependencyInjection;
using Shared.Utils.Services;
using Shared.Services;
using Moq;
using XiansAi.Server.Tests.TestUtils;
using XiansAi.Server.Utils;
using System.Text;
using Shared.Providers.Auth.Auth0;
using Microsoft.AspNetCore.Hosting;
using Shared.Auth;

namespace XiansAi.Server.Tests.IntegrationTests.WebApi;

public class PublicEndpointsTests : WebApiIntegrationTestBase
{
    private const string TestEmail = "test@99x.io";
    private const string ValidCode = "123456";

    public PublicEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
        // Override the factory to include IAuthMgtConnect mock
        _factory = new XiansAiWebApplicationFactory(mongoFixture)
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Add mock for IAuthMgtConnect to handle Auth0 calls
                    var mockAuthMgtConnect = new Mock<IAuthMgtConnect>();
                    mockAuthMgtConnect.Setup(x => x.SetNewTenant(It.IsAny<string>(), It.IsAny<string>()))
                        .ReturnsAsync("success");
                    mockAuthMgtConnect.Setup(x => x.GetUserInfo(It.IsAny<string>()))
                        .ReturnsAsync(new UserInfo { UserId = "test-user" });

                    // Remove existing registration and add mock
                    var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuthMgtConnect));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }
                    services.AddSingleton(mockAuthMgtConnect.Object);
                });
            });
        
        _client = _factory.CreateClient();
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

    [Fact]
    public async Task ValidateCode_WithValidEmailAndCode_ReturnsOK()
    {
        // Arrange - First send a verification code
        await SendVerificationCodeToTestEmail();
        
        var request = new ValidateCodeRequest 
        { 
            Email = TestEmail, 
            Code = await GetCachedVerificationCode(TestEmail)
        };

        // Act
        var response = await PostAsJsonAsync("/api/public/register/verification/validate", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<JsonElement>(response);
        Assert.True(result.GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task ValidateCode_WithInvalidCode_ReturnsOK()
    {
        // Arrange - First send a verification code
        await SendVerificationCodeToTestEmail();
        
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
        // Arrange - First send a verification code and simulate expiration
        await SendVerificationCodeToTestEmail();
        
        // Get the cached code to ensure it exists
        var cachedCode = await GetCachedVerificationCode(TestEmail);
        Assert.NotNull(cachedCode);
        
        // Manually expire the code by removing it from cache
        using var scope = _factory.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ObjectCache>();
        await cache.RemoveAsync($"verification:{TestEmail}");
        
        var request = new ValidateCodeRequest 
        { 
            Email = TestEmail, 
            Code = cachedCode
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