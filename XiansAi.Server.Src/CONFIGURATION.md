# Configuration Management Guide

This guide explains how to use different configuration files when running the XiansAi.Server application.

## Configuration File Hierarchy

The application loads configuration files in the following order (later files override earlier ones):

1. **`appsettings.json`** - Base configuration (always loaded)
2. **`appsettings.{Environment}.json`** - Environment-specific overrides
3. **`appsettings.{ServiceType}.json`** - Service-specific configuration (optional)
4. **`appsettings.{ServiceType}.{Environment}.json`** - Service + environment specific (optional)
5. **Environment Variables** - Highest priority overrides

## Available Configuration Files

- `appsettings.json` - Base configuration with development settings
- `appsettings.Development.json` - Development environment overrides
- `appsettings.Staging.json` - Staging environment overrides  
- `appsettings.Production.json` - Production environment overrides

## Methods to Switch Configurations

### 1. Using Environment Variables (Recommended)

```bash
# Development (uses appsettings.Development.json)
export ASPNETCORE_ENVIRONMENT=Development
dotnet run

# Staging (uses appsettings.Staging.json)
export ASPNETCORE_ENVIRONMENT=Staging
dotnet run

# Production (uses appsettings.Production.json)
export ASPNETCORE_ENVIRONMENT=Production
dotnet run
```

### 2. Using Command Line Arguments

```bash
# Set environment via command line
dotnet run --environment Production
dotnet run --Environment=Staging
```

### 3. Using the Convenience Script

We've provided a helper script for easy environment switching:

```bash
# Run with development environment and all services
./run-environment.sh development all

# Run with production environment and web API only
./run-environment.sh production web

# Run with staging environment and lib API only
./run-environment.sh staging lib

# Short forms also work
./run-environment.sh prod web
./run-environment.sh dev all
```

### 4. Using Docker Compose

```bash
# Development environment (default)
docker-compose up webapi

# Production environment
docker-compose --profile production up webapi-prod

# Staging environment
docker-compose --profile staging up webapi-staging
```

### 5. Using Environment-Specific Docker Compose Files

You can also create separate docker-compose files:

```bash
# Create docker-compose.production.yml
docker-compose -f docker-compose.yml -f docker-compose.production.yml up
```

## Service Types

The application supports running different service combinations:

- **`--web`** - Web API only (user-facing REST API)
- **`--lib`** - Library/Agent API only (internal agent communication)
- **`--all`** - Both APIs (default)

## Configuration Sections

### Key Configuration Sections

- **`MongoDB`** - Database connection settings
- **`Auth0`** - Authentication provider settings
- **`OpenAI`** - AI service configuration
- **`Temporal`** - Workflow engine settings
- **`Cors`** - Cross-origin request settings
- **`Tenants`** - Multi-tenant configuration

### Environment-Specific Differences

| Setting | Development | Staging | Production |
|---------|-------------|---------|------------|
| Database | `99xio_dev` | `xiansai_staging` | `xiansai_prod` |
| Logging Level | `Debug` | `Information` | `Warning` |
| CORS Origins | `localhost:3000/3001` | `staging.xians.ai` | `xians.ai` |

## Environment Variables Override

You can override any configuration value using environment variables with the format:
`SectionName__SubSection__PropertyName`

Examples:
```bash
# Override MongoDB connection string
export MongoDB__ConnectionString="mongodb://localhost:27017"

# Override OpenAI API key
export OpenAI__ApiKey="your-api-key-here"

# Override logging level
export Logging__LogLevel__Default="Information"
```

## Best Practices

1. **Never commit sensitive data** to configuration files
2. **Use environment variables** for secrets in production
3. **Keep environment-specific files minimal** - only override what's different
4. **Use the convenience script** for local development
5. **Test configuration loading** by checking the startup logs

## Troubleshooting

### Configuration Not Loading
- Check that the environment variable `ASPNETCORE_ENVIRONMENT` is set correctly
- Verify the configuration file exists and has valid JSON syntax
- Check the application startup logs for configuration loading messages

### Missing Configuration Values
- Ensure required sections exist in your environment-specific file
- Check if environment variables are overriding expected values
- Verify the configuration hierarchy is working as expected

### Service-Specific Configuration
If you need service-specific configuration, create files like:
- `appsettings.WebApi.json`
- `appsettings.LibApi.json`
- `appsettings.WebApi.Production.json`

## Examples

### Running Different Configurations

```bash
# Development with all services
./run-environment.sh development all

# Production with web API only
ASPNETCORE_ENVIRONMENT=Production dotnet run --web

# Staging with custom MongoDB connection
export ASPNETCORE_ENVIRONMENT=Staging
export MongoDB__ConnectionString="mongodb://staging-server:27017"
dotnet run --all
```

### Docker Examples

```bash
# Development
docker-compose up webapi

# Production
docker-compose --profile production up webapi-prod

# Custom environment
docker run -e ASPNETCORE_ENVIRONMENT=Production xiansai-server
``` 