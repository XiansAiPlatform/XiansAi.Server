# Xians AI Server

This is the server for the Xians AI platform. It is a .NET 7 application that uses the OpenAI API to generate AI responses. It also uses the Temporal API to schedule and run AI workflows.''

## Secrets

The secrets are stored in the Azure Key Vault. The Key Vault is configured in the `appsettings.json` file.

Locate the secrets are in the following file:
~/.microsoft/usersecrets/<user_secrets_id>/secrets.json

refer to link for more information: [link](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-9.0&tabs=linux)

### Running the server

To run the server, you can use the following command:

```bash
dotnet run 
dotnet watch run
```

### Running the Microservices

The application can be run as two separate microservices or as a single combined service. Each microservice can be scaled independently when needed.

#### Microservice Architecture

The application is organized into two main microservices:

1. **WebApi** - Client-facing API endpoints (`api/client/*`)
   - Handles client communication
   - Manages workflows, instructions, and definitions
   - Located in `Features/WebApi` directory

2. **LibApi** - Server-to-server API endpoints (`api/server/*`)
   - Handles internal service communication
   - Processes activities, signals, and agent tasks
   - Located in `Features/LibApi` directory

Both services share common functionality in the `Features/Shared` directory.

Each microservice has its own:

- Configuration files (`appsettings.WebApi.json` and `appsettings.LibApi.json`)
- Service registration classes
- Middleware configuration

#### Using the start script

Run all services (default):

```bash
./start.sh
```

Run only the Web API microservice:

```bash
./start.sh --web
```

Run only the Library API microservice:

```bash
./start.sh --lib
```

Show help:

```bash
./start.sh --help
```

#### Using Docker

Build and run using Docker Compose:

```bash
docker-compose up -d
```

This will start both the Web API service (ports 5000/5001) and the Library API service (ports 5010/5011) as separate containers.

To run all services in a single container, uncomment the "allapi" service in docker-compose.yml:

```bash
# Edit docker-compose.yml to uncomment the allapi service
docker-compose up -d allapi
```

#### Manual Docker Container

You can also run a specific service using Docker directly:

```bash
# Build the Docker image
docker build -t xiansai-server .

# Run the Web API service
docker run -d -p 5000:80 -p 5001:443 -e SERVICE_TYPE=--web --name xiansai-webapi xiansai-server

# Run the Library API service
docker run -d -p 5010:80 -p 5011:443 -e SERVICE_TYPE=--lib --name xiansai-libapi xiansai-server

# Run all services in a single container
docker run -d -p 5020:80 -p 5021:443 -e SERVICE_TYPE=--all --name xiansai-allapi xiansai-server
```

### Auth0 Configuration

The Auth0 configuration is in the `appsettings.json` file.

- Create Auth0 Application
  - These setting are configured in the `Auth0` section of the `appsettings.json` file.
- Create Machine to Machine application authorized to Auth0 Management API
  - These setting are configured in the `Auth0:ManagementApi` section of the `appsettings.json` file.

### Auth0 Custom Action

Note: There is a custom action configured for post Login to send user's tenants with the JWT token.

On Auth0 dashboard go to `Actions->Library->Create from Scratch`

- Name: `Send Tenants to JWT`
- Type: `Login/Post Login`
- Code:

``` javascript
exports.onExecutePostLogin = async (event, api) => {
  const tenantsClaim = 'https://xians.ai/tenants';
  const emailClaim = 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress';
  const { tenants } = event.user.app_metadata;
  console.log(event.user);
  
  if (event.authorization) {
    api.accessToken.setCustomClaim(`${tenantsClaim}`, tenants);
    api.accessToken.setCustomClaim(`${emailClaim}`, event.user.email);
  }
};
```

Now go to `Actions->Triggers->Post Login` and create a flow by dragging the `Send Tenants to JWT` action.

`Start` -> `Send Tenants to JWT` -> `Complete`

### Running the server in production

To run the server in production configuration, you can use the following command:

```bash
dotnet run --launch-profile Production
```

This will use the `appsettings.Production.json` file.

## Deploying the server to Azure Production

To deploy the server to Azure Production, you can use the following command. Ensure you have the correct permissions to deploy to the Azure App Service. Also ensure to clone XiansAi.Infrastructure repository, but run the deploy-webapp.sh script from the XiansAi.Server repository.

```bash
../XiansAi.Infrastructure/Azure/deploy-webapi.sh
```
