# Authentication Provider Configuration

This document provides a comprehensive guide for configuring authentication providers in the XiansAi Server. The system supports multiple authentication providers through a unified interface, allowing you to switch between providers with minimal configuration changes.

## Architecture Overview

The authentication system uses a factory pattern to support multiple providers:

- **Auth0** - Third-party authentication service
- **Azure AD/Entra ID** - Microsoft's identity platform
- **Azure B2C** - Microsoft's customer identity platform
- **Keycloak** - Open-source identity and access management

All providers implement the `IAuthProvider` interface, ensuring consistent behavior across different authentication backends.

## Core Configuration

### Provider Selection

The primary configuration that determines which provider to use:

```bash
# Choose your authentication provider
AuthProvider__Provider=Auth0          # Options: Auth0, AzureB2C, Keycloak
AuthProvider__TenantClaimType=https://your-domain.com/tenants  # Custom claim type for tenant information
```

## Provider-Specific Configuration

### Auth0 Configuration

Auth0 is a popular third-party authentication service that handles user management and authentication flows.

**Required Configuration:**

```bash
AuthProvider__Provider=Auth0

# Auth0 Domain and Audience
Auth0__Domain=your-domain.auth0.com
Auth0__Audience=https://your-api-audience/api

# Management API Credentials (for user management)
Auth0__ManagementApi__ClientId=your-management-client-id
Auth0__ManagementApi__ClientSecret=your-management-client-secret
```

**Key Features:**

- Automatic JWT validation through Auth0's public keys
- Built-in user management through Management API
- Support for custom claims and tenant information
- Automatic role synchronization from database

**Setup Steps:**

1. Create an Auth0 application
2. Configure the audience and domain
3. Set up a Machine-to-Machine application for the Management API
4. Configure custom claims rules for tenant information

### Azure AD/Entra ID Configuration

Azure AD (now called Microsoft Entra ID) is Microsoft's enterprise identity platform.

**Required Configuration:**

```bash
AuthProvider__Provider=AzureB2C

# Azure AD Configuration
AzureB2C__TenantId=your-tenant-id-guid
AzureB2C__Audience=api://your-app-registration-id
AzureB2C__JwksUri=https://login.microsoftonline.com/your-tenant-id/discovery/v2.0/keys
AzureB2C__Issuer=https://sts.windows.net/your-tenant-id/
AzureB2C__Authority=https://login.microsoftonline.com/your-tenant-id/v2.0/

# Optional
AuthProvider__TenantClaimType=https://your-domain.com/tenants

```

**Key Features:**

- Enterprise-grade security and compliance
- Integration with Microsoft ecosystem
- Advanced conditional access policies
- Support for multi-factor authentication

**Setup Steps:**

1. Register an application in Azure AD
2. Configure API permissions and scopes
3. Set up app registration for your API
4. Configure token claims for tenant information

### Azure B2C Configuration

Azure B2C is Microsoft's customer identity platform, designed for customer-facing applications.

**Required Configuration:**

```bash
AuthProvider__Provider=AzureB2C

# Azure B2C Configuration
AzureB2C__TenantId=your-b2c-tenant-id
AzureB2C__Audience=your-app-registration-id
AzureB2C__JwksUri=https://your-tenant.b2clogin.com/your-tenant-id/B2C_1A_SIGNUP_SIGNIN/discovery/v2.0/keys
AzureB2C__Issuer=https://your-tenant.b2clogin.com/your-tenant-id/v2.0/
AzureB2C__Authority=https://your-tenant.b2clogin.com/your-tenant-id/B2C_1A_SIGNUP_SIGNIN/v2.0/

# Optional
AuthProvider__TenantClaimType=https://login-dev.parkly.no/tenants

```

**Key Features:**

- Customer identity and access management
- Custom branding and user experiences
- Social identity provider integration
- Custom user attributes and claims

**Setup Steps:**

1. Create an Azure B2C tenant
2. Set up user flows or custom policies
3. Register your application
4. Configure custom attributes for tenant information

### Keycloak Configuration

Keycloak is an open-source identity and access management solution.

**Required Configuration:**

```bash
AuthProvider__Provider=Keycloak

# Keycloak Configuration
Keycloak__AuthServerUrl=https://your-keycloak-server/
Keycloak__Realm=your-realm-name
Keycloak__ValidIssuer=https://your-keycloak-server/realms/your-realm-name
```

**Key Features:**

- Open-source and self-hosted
- Fine-grained authorization policies
- Federation with external identity providers
- Extensive customization options

**Setup Steps:**

1. Deploy Keycloak server
2. Create a realm for your application
3. Configure clients and users
4. Set up custom attributes for tenant information

## Advanced Configuration

### Token Validation Caching

To improve performance, token validation results can be cached. The cache uses an in-memory store with configurable size limits to prevent memory exhaustion attacks:

```bash
# Token validation cache duration in minutes (default: 5)
Auth__TokenValidationCacheDurationMinutes=5

# Maximum number of cache entries to prevent DoS attacks (default: 10000)
# This limits how many tokens can be cached simultaneously
Auth__TokenValidationCacheSizeLimit=10000

# Size per cache entry for eviction policy (default: 1)
# Used by the cache eviction algorithm when size limit is reached
Auth__TokenValidationCacheEntrySize=1
```

**Security Notes:**

- Only successful token validations are cached to prevent cache poisoning
- Cache uses SHA-256 hashes of tokens as keys to avoid storing sensitive data
- Cache entries use normal priority to allow proper eviction under memory pressure
- Failed validations always trigger fresh validation to prevent attacks

### Certificate Validation Caching (Agent API)

The Agent API uses certificate-based authentication and caches validation results for performance:

```bash
# Certificate validation cache duration in minutes (default: 10)
AgentApi__CertificateValidationCacheDurationMinutes=10

# Size per cache entry for eviction policy (default: 1)
AgentApi__CertificateValidationCacheEntrySize=1
```

**Security Notes:**

- Only successful certificate validations are cached
- Cache is automatically invalidated when a certificate is revoked
- Uses the same global cache size limit as token validation
- Failed validations always trigger fresh validation

### SSL and Security Settings

For production environments:

```bash
# Ensure HTTPS is required (set to true in production)
Auth__RequireHttpsMetadata=true

```

### Development vs Production

**Development Settings:**

```bash
ASPNETCORE_ENVIRONMENT=Development
Auth__RequireHttpsMetadata=false  # Allow HTTP for local development
```

**Production Settings:**

```bash
ASPNETCORE_ENVIRONMENT=Production
Auth__RequireHttpsMetadata=true   # Require HTTPS
```

## Multi-Tenant Support

All providers support multi-tenant configurations through custom claims:

```bash
# Custom claim type for tenant information
AuthProvider__TenantClaimType=https://your-domain.com/tenants
```

**How it works:**

1. The authentication provider includes tenant information in JWT tokens
2. The system extracts tenant IDs from the custom claim
3. User roles are loaded based on the tenant context
4. API endpoints validate tenant access automatically

## Configuration Validation

The system validates configuration at startup and will throw detailed error messages if required settings are missing:

- **Auth0**: Requires `Domain` and `Audience`
- **Azure B2C**: Requires `TenantId`, `Audience`, `JwksUri`, and `Issuer`
- **Keycloak**: Requires `AuthServerUrl` and `Realm`

## Security Best Practices

1. **Use HTTPS in production** - Always require HTTPS for token validation
2. **Rotate secrets regularly** - Change Management API credentials periodically
3. **Limit token lifetime** - Configure appropriate token expiration times
4. **Validate audiences** - Ensure tokens are intended for your API
5. **Monitor authentication logs** - Track failed authentication attempts
6. **Use strong certificates** - Implement proper certificate management

## Migration Between Providers

To migrate from one provider to another:

1. Set up the new provider configuration
2. Update the `AuthProvider__Provider` setting
3. Migrate user data if necessary
4. Update frontend authentication flows
5. Test thoroughly before production deployment

The unified interface ensures that API endpoints don't need to change when switching providers.
