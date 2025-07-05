# XiansAi.Server Start Options

This document describes how to start the XiansAi.Server application in development environment using `dotnet run`, including options for specifying environment files and selecting specific microservices.

## Overview

The XiansAi.Server application is a multi-service application that can run:
- **WebApi**: Main web API service
- **LibApi**: Agent API service (library/agent interactions)
- **UserApi**: User-facing API service
- **All**: All services combined (default)

## Basic Usage

```bash
# Start all services (default behavior)
dotnet run

# Start all services explicitly
dotnet run -- --all

# Show help information
dotnet run -- --help
# or
dotnet run -- -h
```

**Note**: When using `dotnet run`, application arguments must be passed after double dashes (`--`) to separate them from `dotnet run` options.

## Microservice Selection

You can start specific microservices using command line arguments:

### Start Individual Services

```bash
# Start only the Web API service
dotnet run -- --web

# Start only the Agent API service (LibApi)
dotnet run -- --lib

# Start only the User API service
dotnet run -- --user
```

### Service Descriptions

- **`--web`**: Starts the WebApi service
  - Includes web endpoints for main application functionality
  - Handles agent management, workflows, tenants, etc.

- **`--lib`**: Starts the LibApi (Agent API) service
  - Provides agent-specific endpoints
  - Handles agent authentication and operations

- **`--user`**: Starts the UserApi service
  - Provides user-facing functionality
  - Includes webhook and websocket support

- **`--all`**: Starts all services (default)
  - Runs WebApi, LibApi, and UserApi together

## Environment File Configuration

The application supports multiple ways to specify environment files:

### Default Environment File Loading

The application automatically loads environment files in the following order:

1. **Default `.env` file** (if it exists)
2. **Environment-specific file** based on `ASPNETCORE_ENVIRONMENT`:
   - **Development**: `.env.development`
   - **Production**: `.env.production`

```bash
# Uses default environment file loading
dotnet run

# Set environment and use corresponding file
ASPNETCORE_ENVIRONMENT=Development dotnet run  # Uses .env.development
ASPNETCORE_ENVIRONMENT=Production dotnet run   # Uses .env.production
```

### Custom Environment File

You can specify a custom environment file using the `--env-file` argument:

```bash
# Using --env-file=filepath format
dotnet run -- --env-file=.env.local

# Using --env-file filepath format (separate arguments)
dotnet run -- --env-file .env.staging

# Custom env file with specific service
dotnet run -- --web --env-file=.env.testing
```

## Combined Usage Examples

You can combine service selection with environment file specification:

```bash
# Start WebApi service with custom environment file
dotnet run -- --web --env-file=.env.local

# Start LibApi service with staging environment
dotnet run -- --lib --env-file=.env.staging

# Start UserApi service with production environment file
dotnet run -- --user --env-file=.env.production

# Start all services with custom environment
dotnet run -- --all --env-file=.env.development
```

## Environment File Priority

The environment file loading follows this priority:

1. **Custom file specified with `--env-file`** (highest priority)
2. **Default `.env` file** (always loaded first if exists)
3. **Environment-specific file** (`.env.development` or `.env.production`)

If a custom environment file is specified, it takes precedence over the automatic environment-based file selection.

## Example Environment Files

### `.env.development`
```env
ASPNETCORE_ENVIRONMENT=Development
MongoDB__ConnectionString=mongodb://localhost:27017
CONFIG_NAME=Development
```

### `.env.production`
```env
ASPNETCORE_ENVIRONMENT=Production
MongoDB__ConnectionString=mongodb://prod-server:27017
CONFIG_NAME=Production
```

### `.env.local`
```env
ASPNETCORE_ENVIRONMENT=Development
MongoDB__ConnectionString=mongodb://localhost:27017
CONFIG_NAME=LocalDevelopment
```

## Development Workflow Examples

### Local Development
```bash
# Standard local development
dotnet run

# Test specific service locally
dotnet run -- --web --env-file=.env.local
```

### Testing Different Configurations
```bash
# Test with staging configuration
dotnet run -- --env-file=.env.staging

# Test UserApi with production-like settings
dotnet run -- --user --env-file=.env.production
```

### Service-Specific Development
```bash
# Work on WebApi features only
dotnet run -- --web

# Focus on Agent API development
dotnet run -- --lib --env-file=.env.agent-dev
```

## Error Handling

The application will:
- Throw `FileNotFoundException` if a specified custom environment file doesn't exist
- Continue with existing environment variables if default environment files are missing
- Log warnings for missing critical configuration values
- Provide clear error messages for invalid command line arguments

## Configuration Validation

The application validates critical configuration on startup:
- MongoDB connection string
- CONFIG_NAME setting
- Logs warnings for missing values

## Help and Usage Information

The application includes built-in help that shows all available options:

```bash
# Show comprehensive help information
dotnet run -- --help
dotnet run -- -h
```

This will display:
- All available service options (--web, --lib, --user, --all)
- Environment file options (--env-file)
- Usage examples
- Service descriptions
- Environment file loading priority

## Notes

- If no arguments are provided, the application defaults to running all services
- Environment files are loaded using the DotNetEnv library
- The `ASPNETCORE_ENVIRONMENT` variable determines which default environment file to use
- Custom environment files specified with `--env-file` override automatic file selection
- Unknown command line arguments will show an error message with a suggestion to use `--help`
