# Security Headers Implementation

## Overview

This document describes the security headers implementation in XiansAi.Server. These headers protect against common web vulnerabilities including XSS attacks, clickjacking, MIME-sniffing attacks, and other security threats.

## Implementation Location

- **Configuration Class**: `Shared/Configuration/SecurityHeadersConfiguration.cs`
- **Applied In**: `Shared/Configuration/SharedConfiguration.cs` via `UseSecurityHeaders()` middleware

## Security Headers Implemented

### 1. Content-Security-Policy (CSP)

**Purpose**: Mitigates Cross-Site Scripting (XSS) attacks by controlling which resources can be loaded.

**Development Environment** (More permissive for development tools):
```
default-src 'self';
script-src 'self' 'unsafe-inline' 'unsafe-eval';
style-src 'self' 'unsafe-inline';
img-src 'self' data: https:;
font-src 'self' data:;
connect-src 'self' ws: wss: http: https:;
frame-ancestors 'none';
base-uri 'self';
form-action 'self';
object-src 'none';
upgrade-insecure-requests
```

**Production Environment** (Strict policy):
```
default-src 'self';
script-src 'self';
style-src 'self';
img-src 'self' data: https:;
font-src 'self';
connect-src 'self' wss: https:;
frame-ancestors 'none';
base-uri 'self';
form-action 'self';
object-src 'none';
upgrade-insecure-requests
```

**Protection Against**:
- XSS attacks
- Code injection
- Unauthorized resource loading

### 2. X-Content-Type-Options

**Value**: `nosniff`

**Purpose**: Prevents browsers from MIME-sniffing a response away from the declared content-type.

**Protection Against**:
- MIME-sniffing attacks
- Content-type confusion attacks

### 3. X-Frame-Options

**Value**: `DENY`

**Purpose**: Prevents the page from being embedded in iframes, frames, or objects.

**Protection Against**:
- Clickjacking attacks
- UI redressing attacks

### 4. X-XSS-Protection

**Value**: `1; mode=block`

**Purpose**: Enables XSS filtering in legacy browsers.

**Note**: This header is deprecated in modern browsers (which rely on CSP), but still provides protection for older browsers.

**Protection Against**:
- Reflected XSS attacks in legacy browsers

### 5. Referrer-Policy

**Value**: `strict-origin-when-cross-origin`

**Purpose**: Controls how much referrer information is shared when navigating to other sites.

**Behavior**:
- Same-origin requests: Full URL is sent
- Cross-origin requests: Only origin is sent (HTTPS → HTTPS)
- Downgrade requests: No referrer sent (HTTPS → HTTP)

**Protection Against**:
- Information leakage
- Privacy violations

### 6. Permissions-Policy

**Value**: `geolocation=(), microphone=(), camera=(), payment=(), usb=(), magnetometer=(), gyroscope=(), accelerometer=()`

**Purpose**: Controls which browser features and APIs can be used by the page and embedded content.

**Features Disabled**:
- Geolocation
- Microphone access
- Camera access
- Payment API
- USB access
- Magnetometer
- Gyroscope
- Accelerometer

**Protection Against**:
- Unauthorized feature usage
- Privacy violations
- Malicious embedded content

### 7. X-Permitted-Cross-Domain-Policies

**Value**: `none`

**Purpose**: Restricts Adobe Flash and PDF cross-domain policies.

**Protection Against**:
- Cross-domain data access via Adobe products
- Legacy plugin vulnerabilities

### 8. Strict-Transport-Security (HSTS)

**Value**: `max-age=31536000; includeSubDomains; preload`

**When Applied**: Production environment only

**Purpose**: Forces browsers to only connect via HTTPS for the specified duration (1 year).

**Features**:
- `max-age=31536000`: 1 year validity
- `includeSubDomains`: Applies to all subdomains
- `preload`: Eligible for browser preload lists

**Protection Against**:
- Man-in-the-middle attacks
- Protocol downgrade attacks
- Cookie hijacking

## Environment-Specific Behavior

### Development Environment

- **CSP**: More permissive to allow hot-reload, inline scripts, and debugging tools
- **HSTS**: Not applied (allows HTTP connections for local development)
- **All other headers**: Applied with same strictness as production

### Production Environment

- **CSP**: Strict policy - no inline scripts/styles, limited resource sources
- **HSTS**: Applied with 1-year duration and preload
- **All headers**: Fully enabled with maximum security

## Customization

If you need to customize the CSP policy for your specific use case:

1. **Edit**: `Shared/Configuration/SecurityHeadersConfiguration.cs`
2. **Modify**: `BuildProductionCspPolicy()` or `BuildDevelopmentCspPolicy()` methods
3. **Common customizations**:
   - Allow specific external domains: Add to `connect-src` or other directives
   - Enable inline styles/scripts: Add `'unsafe-inline'` (not recommended for production)
   - Allow specific CDNs: Add domains to relevant directives

### Example: Allow Specific External API

```csharp
"connect-src 'self' wss: https: https://api.example.com"
```

### Example: Allow Specific Font CDN

```csharp
"font-src 'self' https://fonts.googleapis.com https://fonts.gstatic.com"
```

## Testing Security Headers

### Using Browser DevTools

1. Open your application in a browser
2. Open DevTools (F12)
3. Go to Network tab
4. Refresh the page
5. Click on the main document request
6. Check the Response Headers section

### Using curl

```bash
curl -I https://your-domain.com
```

### Using Online Tools

- [Security Headers](https://securityheaders.com/)
- [Mozilla Observatory](https://observatory.mozilla.org/)

## Compliance

These headers help meet various security compliance requirements:

- **OWASP Top 10**: Addresses A03:2021 – Injection and A05:2021 – Security Misconfiguration
- **PCI DSS**: Helps meet requirement 6.5.7 (Cross-site scripting prevention)
- **GDPR**: Supports privacy requirements through Referrer-Policy and Permissions-Policy
- **SOC 2**: Demonstrates security controls and configuration management

## Additional Resources

- [OWASP Secure Headers Project](https://owasp.org/www-project-secure-headers/)
- [MDN Web Security](https://developer.mozilla.org/en-US/docs/Web/Security)
- [Content Security Policy Guide](https://content-security-policy.com/)
- [Security Headers Best Practices](https://securityheaders.com/)

## Troubleshooting

### CSP Violations

If you see CSP violations in the browser console:

1. Check the violation report in DevTools Console
2. Identify the blocked resource
3. Update the appropriate CSP directive in `SecurityHeadersConfiguration.cs`
4. Test thoroughly before deploying to production

### CORS Issues

If you encounter CORS issues after implementing security headers:

1. Ensure your CORS configuration in `CorsConfiguration.cs` is correct
2. Verify that `X-Frame-Options` isn't blocking legitimate iframe usage
3. Check that `connect-src` in CSP allows your API endpoints

### Swagger/OpenAPI Issues

If Swagger UI stops working:

1. The development CSP already allows inline scripts/styles for Swagger
2. If issues persist, check browser console for specific CSP violations
3. Ensure `/swagger` endpoints are excluded from strict CSP if needed

## Maintenance

- **Review Regularly**: Security headers should be reviewed quarterly
- **Monitor CSP Reports**: Consider implementing CSP reporting endpoint
- **Stay Updated**: Follow security advisories for header best practices
- **Test Changes**: Always test header changes in development before production deployment

