## Xians AI Server

This is the server for the Xians AI platform. It is a .NET 7 application that uses the OpenAI API to generate AI responses. It also uses the Temporal API to schedule and run AI workflows.

### Running the server

To run the server, you can use the following command:

```bash
dotnet run 
```

### Auth0 Configuration

The Auth0 configuration is in the `appsettings.json` file.

- Create Auth0 Application
  - These setting are configured in the `Auth0` section of the `appsettings.json` file.
- Create Machine to Machine application authorized to Auth0 Management API
  - These setting are configured in the `Auth0:ManagementApi` section of the `appsettings.json` file.

### Running the server in production

To run the server in production configuration, you can use the following command:

```bash
dotnet run --launch-profile Production
```

This will use the `appsettings.Production.json` file.

## Deploying the server to Azure Production

To deploy the server to Azure Production, you can use the following command. Ensure you have the correct permissions to deploy to the Azure App Service. Also ensure to clone XiansAi.Infrastructure repository, but run the deploy-webapp.sh script from the XiansAi.Server repository.

```bash
../XiansAi.Infrastructure/Azure/deploy-webapp.sh
```
