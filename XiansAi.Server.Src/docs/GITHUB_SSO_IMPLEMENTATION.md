# GitHub SSO Implementation Summary

This document provides a technical overview of the GitHub SSO implementation in XiansAi.

## Architecture Overview

GitHub uses OAuth 2.0 for authentication (not OIDC), so it requires a custom implementation separate from our Generic OIDC provider. The flow involves:

1. **OAuth Code Exchange** - User authorizes app on GitHub, returns with authorization code
2. **Token Minting** - Server exchanges code for GitHub access token, fetches user info, and mints a first-party JWT
3. **JWT Validation** - Standard JWT validation for all subsequent API requests

## Server Components

### 1. Configuration Models
**Location**: `Shared/Providers/Auth/GitHub/GitHubConfig.cs`

Defines configuration classes:
- `GitHubOAuthConfig` - GitHub OAuth client credentials and scopes
- `GitHubJwtConfig` - JWT signing configuration (issuer, audience, signing key)
- `GitHubTokenResponse` - GitHub OAuth token response
- `GitHubUser` - GitHub user information
- `GitHubCallbackDto` - Callback request DTO

### 2. JWT Issuer Service
**Location**: `Shared/Services/HmacJwtIssuer.cs`

- `IJwtIssuer` interface for token issuance
- `HmacJwtIssuer` implementation using HMAC-SHA256 signing
- Issues first-party JWTs with configurable TTL (default: 8 hours)
- Registered as singleton in DI container

### 3. GitHub Auth Provider
**Location**: `Shared/Providers/Auth/GitHub/GitHubProvider.cs`

Implements `IAuthProvider` interface:
- `ConfigureJwtBearer()` - Configures JWT validation parameters
- `ValidateToken()` - Validates JWT tokens
- `GetUserInfo()` - Returns user info from JWT claims
- `GetUserTenants()` / `SetNewTenant()` - Tenant management (delegated to database)

Features:
- HMAC-SHA256 JWT validation
- Role enrichment from database via `OnTokenValidated` event
- Integration with existing role/tenant infrastructure

### 4. Public API Endpoint
**Location**: `Features/PublicApi/Endpoints/GitHubAuthEndpoints.cs`

Provides `/api/public/auth/github/callback` endpoint:
1. Exchanges authorization code for GitHub access token
2. Fetches user information from GitHub API
3. Mints first-party JWT with user claims
4. Returns JWT to client

Security features:
- Validates authorization code and redirect URI
- Comprehensive error handling and logging
- Rate limiting via PublicApi middleware

### 5. Provider Registration
**Files Updated**:
- `Shared/Providers/Auth/IAuthProvider.cs` - Added `GitHub` enum value
- `Shared/Providers/Auth/AuthProviderFactory.cs` - Added GitHub provider case
- `Shared/Configuration/SharedConfiguration.cs` - Registered services
- `Features/PublicApi/Configuration/PublicApiConfiguration.cs` - Mapped endpoints

## UI Components

### 1. GitHub Auth Service
**Location**: `XiansAi.UI/src/modules/Manager/auth/github/GitHubService.js`

Main authentication service:
- `init()` - Initializes service, validates stored tokens
- `login()` - Starts OAuth flow with CSRF protection
- `handleRedirectCallback()` - Handles OAuth callback, exchanges code
- `logout()` - Clears tokens and redirects to login
- `getAccessToken()` - Returns validated token

Features:
- CSRF protection via random state parameter
- JWT validation (expiration check)
- Token storage in localStorage
- Auth state change notifications

### 2. Provider Wrapper
**Location**: `XiansAi.UI/src/modules/Manager/auth/github/GitHubProviderWrapper.js`

React component wrapper:
- Initializes GitHub auth service
- Wraps children with `AuthProvider` context
- Shows loading state during initialization

### 3. Token Service
**Location**: `XiansAi.UI/src/modules/Manager/auth/github/GitHubTokenService.js`

Token management:
- `getAccessToken()` - Retrieves and validates token from localStorage
- Token expiration checking with 5-minute buffer
- Automatic cleanup of expired tokens

### 4. Configuration Integration
**Files Updated**:
- `XiansAi.UI/src/config.js` - Added GitHub config parameters
- `XiansAi.UI/src/modules/Manager/auth/createTokenService.js` - Added GitHub token service
- `XiansAi.UI/src/App.jsx` - Registered GitHub provider wrapper

## Configuration

### Server Configuration (appsettings.json)
```json
{
  "AuthProvider": { "Provider": "GitHub" },
  "GitHubOAuth": {
    "ClientId": "Iv1.xxxxx",
    "ClientSecret": "YOUR_GITHUB_CLIENT_SECRET",
    "Scope": "read:user user:email"
  },
  "GitHubJwt": {
    "Issuer": "xians.ai/github",
    "Audience": "xians.ai/api",
    "SigningKey": "YOUR_LONG_RANDOM_SECRET"
  }
}
```

**Environment Variables (Recommended)**:
- `AuthProvider__Provider=GitHub`
- `GitHubOAuth__ClientId`
- `GitHubOAuth__ClientSecret`
- `GitHubJwt__Issuer`
- `GitHubJwt__Audience`
- `GitHubJwt__SigningKey`

### UI Configuration (.env.local)
```bash
REACT_APP_AUTH_PROVIDER=github
REACT_APP_API_URL=http://localhost:5005
REACT_APP_GITHUB_CLIENT_ID=Iv1.xxxxx
REACT_APP_GITHUB_AUTH_URL=https://github.com/login/oauth/authorize
REACT_APP_GITHUB_REDIRECT_URI=http://localhost:3000/callback
REACT_APP_GITHUB_SCOPE=read:user user:email
```

## Security Features

### CSRF Protection
- Random state parameter generated on login
- State stored in sessionStorage
- Validated on callback before code exchange

### Token Security
- HMAC-SHA256 signing for JWT tokens
- Configurable token expiration (default: 8 hours)
- Clock skew tolerance (2 minutes)
- Automatic token validation before use

### Best Practices Implemented
- Client secret stored in environment variables only
- HTTPS required for production
- Minimal OAuth scopes requested
- Comprehensive error logging (no token logging)
- Rate limiting on public endpoints

## Testing

### Unit Testing
Create tests for:
- `HmacJwtIssuer` - Token issuance and validation
- `GitHubProvider` - JWT validation and claim extraction
- `GitHubAuthEndpoints` - OAuth flow and error handling

### Integration Testing
Test scenarios:
1. Complete OAuth flow (mock GitHub)
2. Token validation and expiration
3. Role enrichment from database
4. Error handling (invalid code, network failures)

### Manual Testing
1. Start server and UI
2. Click "Login" → redirected to GitHub
3. Authorize app → redirected back with code
4. Verify JWT received and stored
5. Call protected API → verify authentication works
6. Test logout flow

## Maintenance

### Adding New Claims
To add custom claims to the JWT:
1. Update `GitHubAuthEndpoints.cs` callback to add claims
2. Claims automatically available in `ClaimsPrincipal`

### Changing Token Expiration
Update `HmacJwtIssuer.Issue()`:
```csharp
var token = jwtIssuer.Issue(claims, TimeSpan.FromHours(24)); // 24 hours
```

### Rotating Signing Keys
1. Update `GitHubJwt:SigningKey` in configuration
2. Restart server (existing tokens will be invalidated)
3. Users will need to re-authenticate

## Related Documentation

- [GitHub SSO Provider Guide](./GITHUB_SSO_PROVIDER.md) - Detailed implementation guide
- [UI Setup Guide](../../../XiansAi.UI/docs/GITHUB_SSO_SETUP_GUIDE.md) - UI configuration
- [Generic OIDC Implementation](./GENERIC_OIDC_IMPLEMENTATION_SUMMARY.md) - For comparison
- [Auth Configuration](./AUTH_CONFIGURATION.md) - General auth setup

## Files Created

### Server
- ✅ `Shared/Providers/Auth/GitHub/GitHubConfig.cs`
- ✅ `Shared/Services/HmacJwtIssuer.cs`
- ✅ `Shared/Providers/Auth/GitHub/GitHubProvider.cs`
- ✅ `Features/PublicApi/Endpoints/GitHubAuthEndpoints.cs`
- ✅ `appsettings.GitHub.example.json`

### UI
- ✅ `src/modules/Manager/auth/github/GitHubService.js`
- ✅ `src/modules/Manager/auth/github/GitHubProviderWrapper.js`
- ✅ `src/modules/Manager/auth/github/GitHubTokenService.js`
- ✅ `docs/GITHUB_SSO_SETUP_GUIDE.md`

### Files Modified

#### Server
- ✅ `Shared/Providers/Auth/IAuthProvider.cs` (added GitHub enum)
- ✅ `Shared/Providers/Auth/AuthProviderFactory.cs` (added GitHub case)
- ✅ `Shared/Configuration/SharedConfiguration.cs` (registered services)
- ✅ `Features/PublicApi/Configuration/PublicApiConfiguration.cs` (mapped endpoints)

#### UI
- ✅ `src/config.js` (added GitHub config)
- ✅ `src/modules/Manager/auth/createTokenService.js` (added GitHub service)
- ✅ `src/App.jsx` (added GitHub provider)

## Quick Start

### Server Setup
1. Create GitHub OAuth App
2. Set environment variables or update `appsettings.Development.json`
3. Run server: `dotnet run`

### UI Setup
1. Copy `.env.example` to `.env.local`
2. Update with GitHub Client ID and API URL
3. Run UI: `npm start`

### Verify
1. Navigate to http://localhost:3000
2. Click "Login" → authorize on GitHub
3. Should be redirected back and authenticated
4. Check browser localStorage for `access_token`

## Support

For issues or questions, refer to:
- Server logs for detailed error messages
- Browser console for client-side errors
- [Troubleshooting section](./GITHUB_SSO_PROVIDER.md#troubleshooting) in main guide

