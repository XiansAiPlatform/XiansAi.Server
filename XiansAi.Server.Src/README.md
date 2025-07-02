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

### Running the server in production

To run the server in production configuration, you can use the following command:

```bash
dotnet run --launch-profile Production
```

### Public Docker Image

To run the server in production configuration using Docker, you can use the following command:

```bash
docker run --rm -it \       
  --env-file .env \
  -p 5001:80 \
  --name xiansai-test \
  99xio/xiansai-server:latest
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

This will start both the Web API service (ports 5000/5001), the Library API service (ports 5010/5011) and the Keycloak Auth Service as separate containers.

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

### Keycloak Configuration

The Keycloak service is configured in `docker-compose.yml`. For development, the Keycloak realm `xianAI` is automatically imported on instance startup using a realm file.

#### Realm File Setup

- The realm file is located in the `<project folder>/keycloak-realm` directory as `keycloak-realm.json`.
- **Important:** The realm file does not include secrets. You must obtain the required secrets (client secrets, etc.) from Azure Key Vault or your admin and replace all 4 `*****` secrets placeholders in the file before starting Keycloak.

**To import the realm automatically in development:**

1. Ensure the secrets are set in `keycloak-realm.json`.
1. Start Keycloak with:

```bash
docker-compose up -d keycloak
```

1. Keycloak will be available at [http://localhost:18080](http://localhost:18080) by default (see your `docker-compose.yml` for details).
1. You can login to Keycloak Admin Console with credentials on the `docker-compose.yml`

#### Keycloak Integration in XiansAI.Server

- The Keycloak configuration for the application is in the `appsettings.json` file.
- Example configuration:

    ```json
    "Keycloak": {
      "Realm": "xianAI",
      "AuthServerUrl": "https://<your-keycloak-domain>",
      "Resource": "server-api",
      "Secret":<your-client-secret>
      "Audience": "server-api, account",
      "ManagementApi": {
         "ClientId": "<your-realm-admin-cilent>",
         "ClientSecret": "<your-adminclient-secret>"
      }
    }
    ```

#### Keycloak Organization Management

- Organizations are managed via the Keycloak Admin REST API.
- When a new tenant is set, the application will:
  1. Check if an organization exists (using `/organizations/?search={tenantId}`).
  2. Create the organization if it does not exist.
  3. Add the user as a member to the organization.

### Running the server in production

To run the server in production configuration, you can use the following command:

```bash
dotnet run --launch-profile Production
```

This will use the `appsettings.Production.json` file.

## Deploying the server to Azure Production

To deploy the server to Azure Production, you can use the following command. Ensure you have the correct permissions to deploy to the Azure App Service. Also ensure to clone XiansAi.Infrastructure repository, but run the deploy-webapp.sh script from the XiansAi.Server repository.

```bash
../XiansAi.Server.Infra/deploy-webapi.sh
```

## Docker Build & Deployment

### Building and Publishing Docker Images

Use the unified script for building and publishing multi-platform Docker images:

```bash
# Set required environment variables
export DOCKERHUB_USERNAME=your-username

# Build and publish with default settings (latest tag)
./docker-build-and-publish.sh

# Build and publish with custom tag
export TAG=v1.0.0
./docker-build-and-publish.sh

# Build and publish with multiple tags
export TAG=v1.0.0
export ADDITIONAL_TAGS="latest,beta,stable"
./docker-build-and-publish.sh

# Use custom image name
export IMAGE_NAME=myorg/myapp
./docker-build-and-publish.sh
```

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `DOCKERHUB_USERNAME` | *required* | Your DockerHub username |
| `IMAGE_NAME` | `xiansai/server` | Base image name |
| `TAG` | `latest` | Primary image tag |
| `ADDITIONAL_TAGS` | *(empty)* | Comma-separated additional tags |
| `DOCKERFILE` | `Dockerfile.production` | Dockerfile to use |
| `PLATFORM` | `linux/amd64,linux/arm64` | Target platforms |

### Example Workflow

```bash
# Complete build and publish workflow
export DOCKERHUB_USERNAME=myusername
export TAG=v1.2.0
export ADDITIONAL_TAGS="latest,stable"

# This will build for multiple platforms and push all tags
./docker-build-and-publish.sh
```

The script automatically:

- Logs into DockerHub
- Creates a buildx builder if needed
- Builds for multiple platforms (AMD64 and ARM64)
- Pushes all specified tags simultaneously
- Provides clear feedback on what was published

## Development

To run the application locally:

```bash
./start.sh
```

To run with custom environment:

```bash
./run-environment.sh
```
