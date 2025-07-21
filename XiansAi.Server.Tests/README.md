# XiansAi Server Integration Tests

This project contains integration tests for the XiansAi Server. The tests use a real certificate for authentication to test the authentication mechanism.

## Quick Start

```bash
# Run all tests with development configuration
ASPNETCORE_ENVIRONMENT=Tests dotnet test

```

## Setup

### Certificate Configuration

1. Create a client certificate for testing:
   - You can use the certificate generator from the main application with the following command:

     ```csharp
     // Add the XiansAi.Server project as a dependency
     using XiansAi.Server.Auth;

     // Generate a test certificate
     var certificateGenerator = new CertificateGenerator(configuration, logger, environment, keyVaultService);
     var certificate = await certificateGenerator.GenerateNewClientCertificate("test-cert", "test-tenant", "test-user");
     
     // Export the certificate to Base64
     var certBytes = certificate.Export(X509ContentType.Cert);
     var base64Cert = Convert.ToBase64String(certBytes);
     Console.WriteLine(base64Cert); // Use this value in your .env file

     ```

   - Make sure the certificate is signed by the same root certificate that the application uses

2. Set up environment variables:
   - Copy the `.env.example` file to `.env` in the test project directory
   - Update the `APP_SERVER_API_KEY` with the Base64-encoded certificate generated in step 1

## Running Tests with Different Environments

### Method 1: Using Environment Variables (Recommended)

```bash
# Run tests with Test configuration
export ASPNETCORE_ENVIRONMENT=Test
dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.CacheEndpointTests.SetAndGetCacheValue_ReturnsExpectedResult"

# Run tests with Staging configuration
export ASPNETCORE_ENVIRONMENT=Staging
dotnet test --filter "FullyQualifiedName~CacheEndpointTests"

# Run tests with Development configuration (default)
export ASPNETCORE_ENVIRONMENT=Development
dotnet test
```

### Method 2: Using the Test Environment Script

```bash
# Run specific test with production configuration
./run-tests-with-environment.sh production "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.CacheEndpointTests.SetAndGetCacheValue_ReturnsExpectedResult"

# Run all cache tests with staging configuration
./run-tests-with-environment.sh staging "FullyQualifiedName~CacheEndpointTests"

# Run all tests with development configuration
./run-tests-with-environment.sh development
```

### Method 3: Using Environment-Specific Test Classes

We've created specialized test base classes for different environments:

- `ProductionIntegrationTestBase` - Tests run with Production configuration
- `StagingIntegrationTestBase` - Tests run with Staging configuration  
- `DevelopmentIntegrationTestBase` - Tests run with Development configuration

Example:
```csharp
public class CacheEndpointProductionTests : ProductionIntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public CacheEndpointProductionTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }
    
    [Fact]
    public async Task SetAndGetCacheValue_ReturnsExpectedResult_WithProductionConfig()
    {
        // This test will automatically use Production configuration
        // ...
    }
}
```

### Method 4: One-Line Commands

```bash
# Production environment
ASPNETCORE_ENVIRONMENT=Production dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.CacheEndpointTests.SetAndGetCacheValue_ReturnsExpectedResult"

# Staging environment
ASPNETCORE_ENVIRONMENT=Staging dotnet test --filter "FullyQualifiedName~CacheEndpointTests"
```

## Configuration Loading in Tests

The test framework loads configuration in this order:

1. **`appsettings.json`** - Base configuration from the main application
2. **`appsettings.{Environment}.json`** - Environment-specific overrides (e.g., `appsettings.Production.json`)
3. **Test overrides** - MongoDB connection strings are automatically overridden to use the test database
4. **Environment variables** - Can override any configuration value

### Running the Tests

To run all tests:

```shell
dotnet test
```

To run all tests and generate a test results HTML file:

```shell
dotnet test --logger "html;LogFileName=test-results.html"
```

To run a specific test:

```shell
dotnet test --filter "FullyQualifiedName~AuthenticationTests.AccessProtectedEndpoint_WithoutCertificate_ReturnsUnauthorized"
```

### Skipped Tests

Some tests are marked with `[Fact(Skip = "Requires proper certificate authentication")]`. These tests require a valid certificate to pass, and are skipped by default. To enable these tests:

1. Generate a valid certificate as described above
2. Add the Base64-encoded certificate to your `.env` file
3. Remove the `Skip` attribute from the test methods

## How Authentication Testing Works

1. The test that doesn't require a certificate (`AccessProtectedEndpoint_WithoutCertificate_ReturnsUnauthorized`) verifies that API endpoints properly reject requests without a valid certificate.
2. The other tests (currently skipped) verify that endpoints accept requests with a valid certificate.

## Environment-Specific Testing Examples

### Testing with Production Configuration

```bash
# Test cache functionality with production settings
ASPNETCORE_ENVIRONMENT=Production dotnet test --filter "FullyQualifiedName~CacheEndpointProductionTests"

# Test specific production scenario
./run-tests-with-environment.sh production "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.CacheEndpointProductionTests.SetAndGetCacheValue_ReturnsExpectedResult_WithProductionConfig"
```

### Testing Configuration Differences

You can verify that different environments load different configurations by:

1. Creating environment-specific test data
2. Running the same test with different environments
3. Asserting that the behavior changes based on configuration

## Troubleshooting

If your tests fail with authentication errors:

1. Check that your `.env` file exists and has the correct Base64-encoded certificate
2. Verify that the certificate is valid and signed by the application's root certificate
3. Make sure the certificate's subject contains the correct organization (O) and organizational unit (OU)
4. Check the logs for more detailed error messages

If your tests fail with configuration errors:

1. Verify that the `ASPNETCORE_ENVIRONMENT` variable is set correctly
2. Check that the environment-specific configuration files exist in the main application
3. Ensure that the test configuration overrides are working correctly
4. Check the test output for configuration loading messages

## Best Practices

1. **Use environment variables** for switching configurations during test runs
2. **Create environment-specific test classes** for tests that need to verify environment-specific behavior
3. **Use the test script** for convenient environment switching
4. **Test critical paths** with production configuration to catch environment-specific issues
5. **Keep test data isolated** between different environment test runs
