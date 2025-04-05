#!/bin/bash

# start.sh - Script to start XiansAi.Server microservices

function show_help {
    echo "Usage: ./start.sh [--web|--lib|--all]"
    echo ""
    echo "Options:"
    echo "  --web     Start only the Web API microservice"
    echo "  --lib     Start only the Library API microservice"
    echo "  --all     Start all microservices (default)"
    echo "  --help    Show this help message"
    echo ""
    echo "Examples:"
    echo "  ./start.sh              # Start all microservices"
    echo "  ./start.sh --web        # Start only Web API microservice"
    echo "  ./start.sh --lib        # Start only Library API microservice"
}

# Default to running all services if no arguments provided
SERVICE_TYPE="--all"

# Process command line arguments
if [ "$#" -gt 0 ]; then
    case "$1" in
        --web|--lib|--all)
            SERVICE_TYPE="$1"
            ;;
        --help)
            show_help
            exit 0
            ;;
        *)
            echo "Error: Unknown option: $1"
            show_help
            exit 1
            ;;
    esac
fi

# Run the application with the specified service type
echo "Starting XiansAi.Server with service type: $SERVICE_TYPE"
dotnet run --project XiansAi.Server.csproj $SERVICE_TYPE 