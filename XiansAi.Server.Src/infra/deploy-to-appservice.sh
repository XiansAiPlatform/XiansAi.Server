#!/bin/bash

# Variables
APP_NAME="parklyai-server-dev"
API_RG="rg-parkly-dev-ai"

# Building and publishing the application
echo "Building and publishing the application..."
# Clean up any existing publish directory
rm -rf ./bin/publish
rm -rf ./bin/publish.zip
rm -rf ./bin/check

dotnet clean
dotnet restore
dotnet build --configuration Release
dotnet publish XiansAi.Server.csproj -c Release -o ./bin/publish

# Create a clean zip file with proper path separators
echo "Creating deployment package..."
powershell.exe -Command "
    # Remove existing zip if it exists
    if (Test-Path './bin/publish.zip') { Remove-Item './bin/publish.zip' -Force }
    
    # Create a temporary directory for the files with correct paths
    \$tempDir = Join-Path (Get-Location) 'bin/temp_publish'
    if (Test-Path \$tempDir) { Remove-Item \$tempDir -Recurse -Force }
    New-Item -ItemType Directory -Path \$tempDir -Force | Out-Null
    
    # Copy all files maintaining directory structure
    \$sourceDir = Join-Path (Get-Location) 'bin/publish'
    Get-ChildItem -Path \$sourceDir -Recurse | ForEach-Object {
        # Convert path to use forward slashes
        \$relativePath = \$_.FullName.Substring(\$sourceDir.Length + 1).Replace('\', '/')
        if (\$_.PSIsContainer) {
            \$targetDir = Join-Path \$tempDir \$relativePath
            New-Item -ItemType Directory -Path \$targetDir -Force | Out-Null
        } else {
            \$targetPath = Join-Path \$tempDir \$relativePath
            \$targetDir = Split-Path -Parent \$targetPath
            if (-not (Test-Path \$targetDir)) {
                New-Item -ItemType Directory -Path \$targetDir -Force | Out-Null
            }
            Copy-Item -Path \$_.FullName -Destination \$targetPath -Force
        }
    }
    
    # Use 7-Zip to create the zip file
    \$zipPath = Join-Path (Get-Location) 'bin/publish.zip'
    if (Test-Path \$zipPath) { Remove-Item \$zipPath -Force }
    
    # Check if 7-Zip is installed
    \$7zipPath = 'C:\Program Files\7-Zip\7z.exe'
    if (-not (Test-Path \$7zipPath)) {
        Write-Error '7-Zip is not installed. Please install 7-Zip and try again.'
        exit 1
    }
    
    # Create zip file using 7-Zip
    & \$7zipPath a -tzip \$zipPath (Join-Path \$tempDir '*') -mx=9
    
    # Clean up temporary directory
    Remove-Item \$tempDir -Recurse -Force
"

# Deploy using the new command
echo "Deploying the application..."
az webapp deploy \
    --name $APP_NAME \
    --resource-group $API_RG \
    --src-path ./bin/publish.zip \
    --type zip

echo "Restarting the web app manually"
# az webapp restart --name $APP_NAME --resource-group $API_RG

echo "Deployment completed! Your API is available at: https://parklyai-server-dev.azurewebsites.net/"


