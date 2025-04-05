# XiansAi Server Integration Tests

This project contains integration tests for the XiansAi Server. The tests use a real certificate for authentication to test the authentication mechanism.

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

### Running the Tests

To run all tests:
```shell
dotnet test
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

## Troubleshooting

If your tests fail with authentication errors:

1. Check that your `.env` file exists and has the correct Base64-encoded certificate
2. Verify that the certificate is valid and signed by the application's root certificate
3. Make sure the certificate's subject contains the correct organization (O) and organizational unit (OU)
4. Check the logs for more detailed error messages 