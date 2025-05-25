#!/bin/bash

# Script to run XiansAi.Server with different environment configurations
# Usage: ./run-environment.sh [environment] [service-type]
# 
# Environments: development, staging, production
# Service types: web, lib, all (default: all)

set -e

# Default values
ENVIRONMENT="development"
SERVICE_TYPE="all"

# Parse arguments
if [ $# -ge 1 ]; then
    ENVIRONMENT=$1
fi

if [ $# -ge 2 ]; then
    SERVICE_TYPE=$2
fi

# Convert environment to proper case
case $ENVIRONMENT in
    "dev"|"development")
        ASPNETCORE_ENV="Development"
        ;;
    "staging"|"stage")
        ASPNETCORE_ENV="Staging"
        ;;
    "prod"|"production")
        ASPNETCORE_ENV="Production"
        ;;
    *)
        echo "Unknown environment: $ENVIRONMENT"
        echo "Valid environments: development, staging, production"
        exit 1
        ;;
esac

# Convert service type to argument
case $SERVICE_TYPE in
    "web"|"webapi")
        SERVICE_ARG="--web"
        ;;
    "lib"|"libapi"|"agent")
        SERVICE_ARG="--lib"
        ;;
    "all"|"both")
        SERVICE_ARG="--all"
        ;;
    *)
        echo "Unknown service type: $SERVICE_TYPE"
        echo "Valid service types: web, lib, all"
        exit 1
        ;;
esac

echo "🚀 Starting XiansAi.Server..."
echo "   Environment: $ASPNETCORE_ENV"
echo "   Service Type: $SERVICE_TYPE ($SERVICE_ARG)"
echo "   Configuration files that will be loaded:"
echo "   - appsettings.json"
echo "   - appsettings.$ASPNETCORE_ENV.json (if exists)"
echo ""

# Set environment variable and run
export ASPNETCORE_ENVIRONMENT=$ASPNETCORE_ENV
dotnet run $SERVICE_ARG 