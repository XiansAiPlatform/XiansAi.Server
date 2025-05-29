#!/bin/bash

# Script to run tests with different environment configurations
# Usage: ./run-tests-with-environment.sh [environment] [test-filter]
# 
# Environments: development, staging, production
# Test filter: optional filter for specific tests

set -e

# Default values
ENVIRONMENT="development"
TEST_FILTER=""

# Parse arguments
if [ $# -ge 1 ]; then
    ENVIRONMENT=$1
fi

if [ $# -ge 2 ]; then
    TEST_FILTER=$2
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

echo "🧪 Running tests with environment: $ASPNETCORE_ENV"

# Build the dotnet test command
TEST_CMD="dotnet test"

# Add test filter if provided
if [ ! -z "$TEST_FILTER" ]; then
    TEST_CMD="$TEST_CMD --filter \"$TEST_FILTER\""
    echo "   Test filter: $TEST_FILTER"
fi

echo "   Configuration files that will be loaded:"
echo "   - appsettings.json"
echo "   - appsettings.$ASPNETCORE_ENV.json (if exists)"
echo ""

# Set environment variable and run tests
export ASPNETCORE_ENVIRONMENT=$ASPNETCORE_ENV
eval $TEST_CMD 