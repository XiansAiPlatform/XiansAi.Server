# HTTPS Enforcement & Security Headers

## Overview
This document describes the HTTPS enforcement and security headers implementation that protects the application in production while maintaining a smooth local development experience.

## Security Features Implemented

### 1. HSTS (HTTP Strict Transport Security)
- **Production**: Enabled with 1-year max-age, including subdomains
- **Development**: Disabled to allow HTTP local development
- **Header**: `Strict-Transport-Security: max-age=31536000; includeSubDomains`

### 2. HTTPS Redirection
- **Production**: All HTTP requests automatically redirect to HTTPS
- **Development**: No redirection, allows HTTP on localhost:5005
- **Exception**: Health check endpoint (`/health`) remains accessible via HTTP for container orchestration

### 3. Secure Cookie Policy
- **Production**: `CookieSecurePolicy.Always` - requires HTTPS for all cookies
- **Development**: `CookieSecurePolicy.None` - allows HTTP cookies
- **SameSite**: Set to `Lax` for OAuth compatibility (blocks CSRF while allowing redirects)

### 4. Additional Security Headers
Applied **only in production**:
- `X-Content-Type-Options: nosniff` - Prevents MIME type sniffing
- `X-Frame-Options: DENY` - Prevents clickjacking attacks
- `X-XSS-Protection: 1; mode=block` - XSS protection for legacy browsers

## Environment-Based Configuration

### Development Environment
```bash
ASPNETCORE_ENVIRONMENT=Development
```

**Behavior:**
- ✅ HTTP allowed (http://localhost:5005)
- ✅ No HTTPS redirection
- ✅ No HSTS headers
- ✅ Cookies work over HTTP
- ✅ No security headers added

**Local Development Setup:**
```bash
# No changes needed! Just run as usual:
dotnet run

# Or with specific service:
dotnet run --user
```

### Production Environment
```bash
ASPNETCORE_ENVIRONMENT=Production
```

**Behavior:**
- ✅ HTTPS enforced (https://+:443)
- ✅ HTTP→HTTPS automatic redirection
- ✅ HSTS headers sent
- ✅ Cookies require HTTPS
- ✅ Security headers added
- ✅ Health check still accessible via HTTP for load balancers

**Docker Production:**
```dockerfile
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080;https://+:443
```

## Testing

### Test HTTPS Enforcement Locally (Optional)
If you want to test HTTPS locally before deploying:

1. **Generate a development certificate:**
   ```bash
   dotnet dev-certs https --trust
   ```

2. **Update launchSettings.json temporarily:**
   ```json
   {
     "applicationUrl": "https://localhost:5001;http://localhost:5005"
   }
   ```

3. **Set environment to Production:**
   ```bash
   export ASPNETCORE_ENVIRONMENT=Production
   dotnet run
   ```

4. **Verify:**
   - HTTP request: `curl -I http://localhost:5005/health`
     - Should return 307/308 redirect to HTTPS
   - HTTPS request: `curl -I -k https://localhost:5001/health`
     - Should return security headers

### Production Deployment Testing

1. **Verify HSTS Header:**
   ```bash
   curl -I https://your-domain.com/health
   # Should include: Strict-Transport-Security: max-age=31536000; includeSubDomains
   ```

2. **Verify HTTP→HTTPS Redirect:**
   ```bash
   curl -I http://your-domain.com/api/some-endpoint
   # Should return 307/308 with Location: https://...
   ```

3. **Verify Security Headers:**
   ```bash
   curl -I https://your-domain.com/api/some-endpoint
   # Should include:
   # - X-Content-Type-Options: nosniff
   # - X-Frame-Options: DENY
   # - X-XSS-Protection: 1; mode=block
   ```

## Load Balancer / Reverse Proxy Configuration

If using a load balancer or reverse proxy (e.g., Azure App Service, AWS ALB, nginx):

### Azure App Service
- The platform handles SSL/TLS termination
- Keep HTTP port 8080 for health checks
- HTTPS redirection middleware will work correctly

### nginx Reverse Proxy
```nginx
server {
    listen 80;
    server_name your-domain.com;
    
    # Let the app handle HTTPS redirection
    location / {
        proxy_pass http://localhost:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}

server {
    listen 443 ssl;
    server_name your-domain.com;
    
    ssl_certificate /path/to/cert.pem;
    ssl_certificate_key /path/to/key.pem;
    
    location / {
        proxy_pass http://localhost:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto https;
    }
}
```

### Docker Compose with Traefik
```yaml
services:
  xiansai:
    image: your-registry/xiansai-server:latest
    environment:
      - ASPNETCORE_ENVIRONMENT=Production
    labels:
      - "traefik.enable=true"
      - "traefik.http.routers.xiansai.rule=Host(`your-domain.com`)"
      - "traefik.http.routers.xiansai.entrypoints=websecure"
      - "traefik.http.routers.xiansai.tls.certresolver=letsencrypt"
      - "traefik.http.services.xiansai.loadbalancer.server.port=8080"
```

## Security Considerations

### Why HTTP Port is Still Exposed
- **Health Checks**: Container orchestrators need HTTP access for liveness/readiness probes
- **Graceful Degradation**: If HTTPS cert expires, HTTP allows emergency access to fix issues
- **Middleware Control**: The app handles redirection, giving fine-grained control

### HSTS Preloading
To submit your domain to the HSTS preload list:
1. Ensure HSTS header includes `preload` directive (already configured)
2. Submit at: https://hstspreload.org/
3. This provides maximum security but is irreversible for ~18 months

### SameSite Cookie Policy
- **Lax** (configured): Allows cookies on top-level navigation (OAuth flows work)
- **Strict** (alternative): Blocks all cross-site cookies (would break OAuth)
- **None** (not recommended): No CSRF protection

## Troubleshooting

### Issue: "This site can't provide a secure connection" in Production
**Cause**: HTTPS not properly configured on the hosting platform

**Solution:**
1. Verify SSL certificate is installed
2. Check ASPNETCORE_URLS includes `https://+:443`
3. Verify firewall allows port 443
4. Check reverse proxy SSL termination settings

### Issue: "Cookies not being set" in Development
**Cause**: Browser enforcing secure cookies despite environment setting

**Solution:**
1. Verify `ASPNETCORE_ENVIRONMENT=Development`
2. Clear browser cookies and cache
3. Check SharedConfiguration.cs shows `CookieSecurePolicy.None` for development

### Issue: "Redirect loop" in Production
**Cause**: Reverse proxy and app both trying to redirect HTTP→HTTPS

**Solution:**
1. Configure proxy to pass `X-Forwarded-Proto` header
2. Or disable app HTTPS redirection and let proxy handle it:
   ```bash
   # Environment variable override:
   DISABLE_HTTPS_REDIRECTION=true
   ```

## Implementation Details

### Code Location
- **File**: `XiansAi.Server.Src/Shared/Configuration/SharedConfiguration.cs`
- **Methods**: 
  - `AddSharedServices()` - Configures HSTS and cookie policies
  - `UseSharedMiddleware()` - Applies middleware based on environment

### Key Code Sections

**HSTS Configuration:**
```csharp
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddHsts(options =>
    {
        options.Preload = true;
        options.IncludeSubDomains = true;
        options.MaxAge = TimeSpan.FromDays(365);
    });
}
```

**Cookie Security:**
```csharp
builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.Secure = builder.Environment.IsProduction() 
        ? CookieSecurePolicy.Always 
        : CookieSecurePolicy.None;
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
});
```

**Middleware Application:**
```csharp
if (app.Environment.IsProduction())
{
    app.UseHsts();
    app.UseHttpsRedirection();
    // Additional security headers...
}
```

## References
- [ASP.NET Core HTTPS Enforcement](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl)
- [HSTS RFC 6797](https://tools.ietf.org/html/rfc6797)
- [OWASP Secure Headers Project](https://owasp.org/www-project-secure-headers/)
- [SameSite Cookie Specification](https://tools.ietf.org/html/draft-ietf-httpbis-cookie-same-site)

