#!/bin/bash

# start.sh - Script to start XiansAi.Server microservices

function show_help {
    echo "Usage: ./start.sh [--web|--lib|--all] [--dotenv <envfile>]"
    echo ""
    echo "Options:"
    echo "  --web        Start only the Web API microservice"
    echo "  --lib        Start only the Library API microservice"
    echo "  --all        Start all microservices (default)"
    echo "  --dotenv     Specify a .env file to load environment variables (default: .env)"
    echo "  --help       Show this help message"
    echo ""
    echo "Examples:"
    echo "  ./start.sh                   # Start all microservices with .env"
    echo "  ./start.sh --web             # Start only Web API microservice with .env"
    echo "  ./start.sh --dotenv XiansAi.Server.Src/.env.parkly  # Use custom env file"
    echo "  ./start.sh --web --dotenv XiansAi.Server.Src/.env.parkly"
}

function check_and_install_nodejs {
    if ! command -v node &> /dev/null; then
        echo "Node.js is not installed. Installing Node.js..."
        if [[ "$OSTYPE" == "darwin"* ]]; then
            # macOS - use Homebrew if available
            if command -v brew &> /dev/null; then
                brew install node
            else
                echo "Please install Homebrew first, then run: brew install node"
                echo "Or download Node.js from: https://nodejs.org/"
                exit 1
            fi
        elif [[ "$OSTYPE" == "linux-gnu"* ]]; then
            # Linux - try common package managers
            if command -v apt-get &> /dev/null; then
                sudo apt-get update && sudo apt-get install -y nodejs npm
            elif command -v yum &> /dev/null; then
                sudo yum install -y nodejs npm
            elif command -v dnf &> /dev/null; then
                sudo dnf install -y nodejs npm
            else
                echo "Please install Node.js manually from: https://nodejs.org/"
                exit 1
            fi
        else
            echo "Please install Node.js manually from: https://nodejs.org/"
            exit 1
        fi
    fi
}

function check_and_install_dotenv_cli {
    # Check if dotenv-cli (Node.js version) is available
    local has_nodejs_dotenv=false
    
    # Check if dotenv-cli is installed via npm
    if npm list -g dotenv-cli &> /dev/null; then
        has_nodejs_dotenv=true
    fi
    
    # Check if the existing dotenv command is the Node.js version
    if command -v dotenv &> /dev/null; then
        # Test if it's the Node.js version by checking for specific syntax
        if dotenv --help 2>&1 | grep -q "\-e.*\-\-env-file"; then
            has_nodejs_dotenv=true
        elif dotenv --help 2>&1 | grep -q "dotenv \[OPTIONS\] COMMAND"; then
            echo "Warning: Python dotenv CLI detected. Need to install Node.js version."
            has_nodejs_dotenv=false
        fi
    fi
    
    if [ "$has_nodejs_dotenv" = false ]; then
        echo "Installing Node.js dotenv-cli..."
        
        # Check if we have npm
        if ! command -v npm &> /dev/null; then
            echo "npm is not available. Checking Node.js installation..."
            check_and_install_nodejs
        fi
        
        # Install dotenv-cli globally, forcing overwrite if needed
        echo "Installing dotenv-cli globally (may overwrite existing dotenv)..."
        npm install -g dotenv-cli --force
        
        if [ $? -ne 0 ]; then
            echo "Failed to install dotenv-cli. Trying alternative approach..."
            # Try uninstalling any existing dotenv packages first
            npm uninstall -g dotenv python-dotenv &> /dev/null
            npm install -g dotenv-cli
            
            if [ $? -ne 0 ]; then
                echo "Failed to install dotenv-cli. Please install it manually:"
                echo "  npm uninstall -g dotenv python-dotenv"
                echo "  npm install -g dotenv-cli --force"
                exit 1
            fi
        fi
    fi
}

# Default to running all services if no arguments provided
SERVICE_TYPE="--all"
DOTENV_FILE=".env"

# Process command line arguments
while [[ $# -gt 0 ]]; do
    case "$1" in
        --web|--lib|--all)
            SERVICE_TYPE="$1"
            shift
            ;;
        --dotenv)
            if [[ -n "$2" ]]; then
                DOTENV_FILE="$2"
                shift 2
            else
                echo "Error: --dotenv requires a file argument"
                show_help
                exit 1
            fi
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
done

# Check and install dependencies
echo "Checking dependencies..."
check_and_install_dotenv_cli

# Verify dotenv-cli is working correctly
echo "Verifying dotenv-cli installation..."
# Try to use the npm-installed version directly
DOTENV_CMD="dotenv"

# Check if we can find the npm global bin path
if command -v npm &> /dev/null; then
    NPM_BIN_PATH=$(npm bin -g 2>/dev/null)
    if [ -n "$NPM_BIN_PATH" ] && [ -f "$NPM_BIN_PATH/dotenv" ]; then
        DOTENV_CMD="$NPM_BIN_PATH/dotenv"
    fi
fi

# Test the dotenv command with a simple version check or help
if ! $DOTENV_CMD --help 2>&1 | grep -q "\-e.*\-\-env-file" && ! $DOTENV_CMD --version &> /dev/null; then
    echo "Warning: dotenv-cli may not be working correctly. Attempting to use anyway..."
    # Don't exit, just warn and try to continue
fi

# Run the application with the specified service type and dotenv file
echo "Starting XiansAi.Server with service type: $SERVICE_TYPE and env file: $DOTENV_FILE"
$DOTENV_CMD -e "$DOTENV_FILE" -- dotnet run --project XiansAi.Server.csproj $SERVICE_TYPE 