# Request Size Limits Implementation

## Overview

Request size limits have been configured globally to prevent Denial of Service (DoS) attacks through large payload submissions. This implementation sets strict boundaries on request sizes at multiple levels.

## Security Issue Addressed

**Issue**: No Request Size Limits  
**Location**: Missing global configuration  
**Impact**: DoS attacks via large payloads  
**Severity**: High  
**Status**: ✅ Fixed

## Implementation Details

### Configuration Structure

Request limits are configured in `appsettings.json` under the `RequestLimits` section:

```json
{
  "RequestLimits": {
    "MaxRequestBodySize": 31457280,
    "MultipartBodyLengthLimit": 134217728,
    "MaxRequestBufferSize": 1048576
  }
}
```

### Default Values

| Setting | Default Value | Description |
|---------|--------------|-------------|
| `MaxRequestBodySize` | 30MB (31,457,280 bytes) | Maximum size for standard request bodies |
| `MultipartBodyLengthLimit` | 128MB (134,217,728 bytes) | Maximum size for file uploads |
| `MaxRequestBufferSize` | 1MB (1,048,576 bytes) | Maximum request buffer size |

### Additional Kestrel Limits

The implementation also configures additional Kestrel server limits:

| Limit | Value | Purpose |
|-------|-------|---------|
| `MaxRequestHeaderCount` | 100 | Prevent header-based DoS attacks |
| `MaxRequestHeadersTotalSize` | 32KB | Limit total header size |
| `MaxRequestLineSize` | 8KB | Limit URL and method line size |

### Form Options Configuration

For multipart/form-data requests (file uploads):

| Option | Value | Description |
|--------|-------|-------------|
| `MultipartBodyLengthLimit` | 128MB | Maximum file upload size |
| `ValueCountLimit` | 1024 | Maximum number of form values |
| `MemoryBufferThreshold` | 64KB | Memory buffer threshold |

## Files Modified

1. **`XiansAi.Server.Src/appsettings.Sample.json`**
   - Added `RequestLimits` configuration section

2. **`XiansAi.Server.Src/Shared/Configuration/RequestLimitsConfiguration.cs`** (New File)
   - Implements request size limit configuration
   - Configures Kestrel server limits
   - Configures form options for file uploads

3. **`XiansAi.Server.Src/Shared/Configuration/SharedConfiguration.cs`**
   - Integrated request limits configuration via `.AddRequestLimits()` extension method

## Configuration

### Environment-Specific Limits

You can configure different limits for different environments by overriding values in environment-specific configuration files:

**Development (.env.development or appsettings.Development.json):**
```json
{
  "RequestLimits": {
    "MaxRequestBodySize": 52428800,
    "MultipartBodyLengthLimit": 209715200
  }
}
```

**Production (.env.production or appsettings.Production.json):**
```json
{
  "RequestLimits": {
    "MaxRequestBodySize": 31457280,
    "MultipartBodyLengthLimit": 134217728
  }
}
```

### Per-Endpoint Overrides

For specific endpoints that need custom limits, use the `[RequestSizeLimit]` attribute:

```csharp
[HttpPost("upload")]
[RequestSizeLimit(104857600)] // 100MB for this specific endpoint
public async Task<IActionResult> UploadLargeFile([FromForm] IFormFile file)
{
    // Handle large file upload
}
```

Or disable the limit for specific endpoints (use with caution):

```csharp
[HttpPost("stream")]
[DisableRequestSizeLimit]
public async Task<IActionResult> StreamData()
{
    // Handle streaming data
}
```

## Security Benefits

1. **DoS Attack Prevention**: Prevents attackers from exhausting server resources by sending extremely large requests
2. **Memory Protection**: Limits memory consumption from buffering large requests
3. **Header Attack Protection**: Prevents header-based DoS attacks through header count and size limits
4. **URL Attack Protection**: Limits URL length to prevent URL-based attacks

## Monitoring

The application logs the configured request limits on startup:

```
Request limits configured - MaxRequestBodySize: 30MB, MultipartBodyLengthLimit: 128MB, MaxRequestBufferSize: 1024KB
```

## Testing

### Test Scenarios

1. **Normal Request**: Verify requests under the limit work correctly
2. **Oversized Request Body**: Test that requests exceeding `MaxRequestBodySize` are rejected
3. **Large File Upload**: Test that file uploads exceeding `MultipartBodyLengthLimit` are rejected
4. **Header Limits**: Test that excessive headers are rejected

### Example Test

```bash
# Test with a request body exceeding the limit
curl -X POST https://your-api.com/api/endpoint \
  -H "Content-Type: application/json" \
  -d @large_file.json

# Expected response: 413 Payload Too Large
```

## Error Responses

When request size limits are exceeded, clients receive:

- **Status Code**: `413 Payload Too Large`
- **Response**: Standard ASP.NET Core error response

Example:
```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.11",
  "title": "Payload Too Large",
  "status": 413
}
```

## Best Practices

1. **Set Appropriate Limits**: Configure limits based on your application's actual needs
2. **Monitor Usage**: Track request sizes in your logs to identify if limits need adjustment
3. **Document Limits**: Inform API consumers about request size limits in your API documentation
4. **Environment-Specific**: Use different limits for development (more permissive) and production (more restrictive)
5. **Per-Endpoint Customization**: Apply custom limits only where necessary using attributes

## Related Security Features

This implementation complements other security features:

- [Rate Limiting](RATE_LIMITING_IMPLEMENTATION.md)
- [Security Headers](SECURITY_HEADERS.md)
- [HTTPS Enforcement](HTTPS_ENFORCEMENT.md)

## References

- [ASP.NET Core Kestrel Options](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/options)
- [ASP.NET Core Request Size Limits](https://learn.microsoft.com/en-us/aspnet/core/mvc/models/file-uploads#file-upload-limits)
- [OWASP DoS Prevention](https://cheatsheetseries.owasp.org/cheatsheets/Denial_of_Service_Cheat_Sheet.html)

## Maintenance

### Reviewing Limits

Periodically review and adjust limits based on:
- Application requirements
- User feedback
- Security audits
- Performance metrics

### Updating Configuration

To update limits:
1. Modify values in `appsettings.json` or environment-specific config files
2. Restart the application for changes to take effect
3. Monitor application logs and performance after changes

## Troubleshooting

### Issue: Legitimate requests being rejected

**Solution**: Increase the appropriate limit in configuration:
```json
{
  "RequestLimits": {
    "MaxRequestBodySize": 52428800  // Increase to 50MB
  }
}
```

### Issue: File uploads failing

**Solution**: Check and increase `MultipartBodyLengthLimit`:
```json
{
  "RequestLimits": {
    "MultipartBodyLengthLimit": 209715200  // Increase to 200MB
  }
}
```

### Issue: Forms with many fields failing

**Solution**: Adjust `ValueCountLimit` in form options (requires code change in `RequestLimitsConfiguration.cs`)

