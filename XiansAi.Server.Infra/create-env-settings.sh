#!/bin/bash

# Variables
APP_NAME="parklyai-server-dev"
RESOURCE_GROUP="rg-parkly-dev-ai"
SETTINGS_FILE="../XiansAi.Server.Src/appsettings.json"

# Check if jq is installed
if ! command -v jq &> /dev/null; then
    echo "jq is not installed. Please install it first."
    exit 1
fi

# Function to flatten JSON and create key-value pairs
flatten_json() {
    local prefix=$1
    local json=$2
    
    echo "$json" | jq -r '
    def flatten($prefix):
        . as $in
        | keys[] as $k
        | $in[$k] as $v
        | if ($v|type) == "object" then
            $v | flatten($prefix + $k + "__")
          else
            $prefix + $k + "=" + ($v|tostring)
          end;
    flatten("")'
}

# Read the settings file
if [ ! -f "$SETTINGS_FILE" ]; then
    echo "Error: $SETTINGS_FILE not found"
    exit 1
fi

# Get the JSON content
json_content=$(cat "$SETTINGS_FILE")

echo "=== Starting settings upload ==="
echo ""

# Process and upload settings one by one
while IFS="=" read -r key value; do
    echo "Uploading: $key"
    echo "Value: $value"
    
    # Upload single setting to Azure
    az webapp config appsettings set \
      --name "$APP_NAME" \
      --resource-group "$RESOURCE_GROUP" \
      --settings "$key=$value"
    
    echo "---"
done < <(flatten_json "" "$json_content")

echo ""
echo "=== Settings upload completed ==="