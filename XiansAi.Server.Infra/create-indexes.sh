#!/bin/bash
 
# Set default connection string and database name
DEFAULT_CONNECTION_STRING="mongodb://parklyai-dev:YIaBS6r1mGjjHcedX38wJWt6wVS83lYhHZdDgsHgfbQ7HtHlgTEy0zlqjSBspEJnc0YSp9rJIMapACDb2kKKnQ==@parklyai-dev.mongo.cosmos.azure.com:10255/?ssl=true&replicaSet=globaldb&retrywrites=false&maxIdleTimeMS=120000&appName=@parklyai-dev@"
DEFAULT_DATABASE_NAME="parklyai"
DEFAULT_ACCOUNT_NAME="parklyai-dev"
DEFAULT_RESOURCE_GROUP="rg-parkly-dev-ai"  # Replace with your actual resource group name
 
# Use provided connection string or default
CONNECTION_STRING=${1:-$DEFAULT_CONNECTION_STRING}
DATABASE_NAME=${2:-$DEFAULT_DATABASE_NAME}
ACCOUNT_NAME=${3:-$DEFAULT_ACCOUNT_NAME}
RESOURCE_GROUP=${4:-$DEFAULT_RESOURCE_GROUP}
 
# Function to create indexes for a collection
create_collection_indexes() {
    local collection_name=$1
    local index_definition=$2
   
    echo "Creating indexes for collection: $collection_name"
    az cosmosdb mongodb collection update \
        --resource-group $RESOURCE_GROUP \
        --account-name $ACCOUNT_NAME \
        --database-name $DATABASE_NAME \
        --name $collection_name \
        --idx "$index_definition"
}
 
# Conversation Messages Collection Indexes
conversation_messages_indexes='[
    {
        "key": {
            "keys": ["_id"],
            "name": "_id_"
        }
    },
    {
        "key": {
            "keys": ["tenant_id", "thread_id", "created_at"],
            "name": "thread_message_lookup"
        }
    },
    {
        "key": {
            "keys": ["tenant_id", "status"],
            "name": "message_status_lookup"
        }
    },
    {
        "key": {
            "keys": ["tenant_id", "participant_id", "workflow_id"],
            "name": "participant_workflow_lookup"
        }
    },
    {
        "key": {
            "keys": ["thread_id", "created_at"],
            "name": "thread_timestamp_lookup"
        }
    }
]'
 
# Agents Collection Indexes
agents_indexes='[
    {
        "key": { "keys": ["_id"], "name": "_id_" }
    },
    {
        "key": { "keys": ["name", "tenant"], "name": "agent_name_tenant_lookup", "unique": true }
    },
    {
        "key": { "keys": ["tenant", "owner_access"], "name": "tenant_owner_access_index" }
    },
    {
        "key": { "keys": ["tenant", "read_access"], "name": "tenant_read_access_index" }
    },
    {
        "key": { "keys": ["tenant", "write_access"], "name": "tenant_write_access_index" }
    }
]'
 
# Flow Definitions Collection Indexes
flow_definitions_indexes='[
    {
        "key": { "keys": ["_id"], "name": "_id_" }
    },
    {
        "key": { "keys": ["agent", "created_at"], "name": "agent_created_at_index" }
    }
]'

 
# Conversation Threads Collection Indexes
conversation_threads_indexes='[
    {
        "key": {
            "keys": ["_id"],
            "name": "_id_"
        }
    },
    {
        "key": {
            "keys": ["tenant_id", "participant_id"],
            "name": "thread_participant_lookup"
        }
    },
    {
        "key": {
            "keys": ["tenant_id", "updated_at"],
            "name": "thread_timestamp_lookup"
        }
    }
]'

# Logs Collection Indexes
logs_indexes='[
    {
        "key": {
            "keys": ["_id"],
            "name": "_id_"
        }
    },
    {
        "key": {
            "keys": ["tenant_id", "created_at"],
            "name": "tenant_createdAt_index"
        }
    },
    {
        "key": {
            "keys": ["workflow_id", "workflow_run_id"],
            "name": "workflow_id_run_id_index"
        }
    },
    {
        "key": {
            "keys": ["level"],
            "name": "level_index"
        }
    },
    {
        "key": {
            "keys": ["tenant_id", "agent", "participant_id"],
            "name": "tenant_agent_participant_index"
        }
    },
    {
        "key": {
            "keys": ["tenant_id", "agent", "workflow_type"],
            "name": "tenant_agent_workflowtype_index"
        }
    },
    {
        "key": {
            "keys": ["tenant_id", "agent", "participant_id", "workflow_type"],
            "name": "tenant_agent_participant_workflowtype_index"
        }
    }
]'

# Create indexes for each collection
#create_collection_indexes "conversation_message" "$conversation_messages_indexes"
#create_collection_indexes "agents" "$agents_indexes"
create_collection_indexes "flow_definitions" "$flow_definitions_indexes"
#create_collection_indexes "conversation_thread" "$conversation_threads_indexes"
#create_collection_indexes "logs" "$logs_indexes"

echo "All indexes have been created successfully!"