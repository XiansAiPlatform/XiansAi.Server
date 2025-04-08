#!/bin/bash

# Variables
APP_NAME="xiansai-server"
API_RG="rg-api"

# Building and publishing the application
echo "Building and publishing the application..."
# Clean up any existing publish directory
rm -rf ./bin/publish
rm -rf ./bin/publish.zip

dotnet clean
dotnet restore
dotnet build --configuration Release
dotnet publish XiansAi.Server.csproj -c Release -o ./bin/publish

# Zip the published files
cd bin/publish && zip -r ../publish.zip . && cd ../..

# Deploy using the new command
echo "Deploying the application..."
az webapp deploy \
    --name $APP_NAME \
    --resource-group $API_RG \
    --src-path ./bin/publish.zip \
    --type zip

echo "Restarting the web app..."
az webapp restart --name $APP_NAME --resource-group $API_RG

echo "Deployment completed! Your API is available at: https://api.xians.ai"


