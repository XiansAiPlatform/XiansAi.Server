#!/bin/bash

# Base64 Encode File Script
# Usage: ./base64-encode.sh <input_file> [output_file]
# If output_file is not provided, it will create <input_file>.b64

# Function to display usage
show_usage() {
    echo "Usage: $0 <input_file> [output_file]"
    echo ""
    echo "Arguments:"
    echo "  input_file   - The file to base64 encode"
    echo "  output_file  - Optional. Output file name (default: <input_file>.b64)"
    echo ""
    echo "Examples:"
    echo "  $0 myfile.txt"
    echo "  $0 myfile.txt encoded_output.txt"
    exit 1
}

# Function to log messages
log_message() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1"
}

# Function to log errors
log_error() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] ERROR: $1" >&2
}

# Check if at least one argument is provided
if [ $# -lt 1 ]; then
    log_error "Missing required argument: input_file"
    show_usage
fi

# Get input file from first argument
INPUT_FILE="$1"

# Validate input file exists
if [ ! -f "$INPUT_FILE" ]; then
    log_error "Input file '$INPUT_FILE' does not exist or is not a regular file"
    exit 1
fi

# Check if input file is readable
if [ ! -r "$INPUT_FILE" ]; then
    log_error "Input file '$INPUT_FILE' is not readable"
    exit 1
fi

# Determine output file name
if [ $# -ge 2 ]; then
    OUTPUT_FILE="$2"
else
    OUTPUT_FILE="${INPUT_FILE}.b64"
fi

# Check if output file already exists and warn user
if [ -f "$OUTPUT_FILE" ]; then
    log_message "Warning: Output file '$OUTPUT_FILE' already exists and will be overwritten"
fi

# Check if output directory is writable
OUTPUT_DIR=$(dirname "$OUTPUT_FILE")
if [ ! -w "$OUTPUT_DIR" ]; then
    log_error "Output directory '$OUTPUT_DIR' is not writable"
    exit 1
fi

# Perform base64 encoding
log_message "Encoding file '$INPUT_FILE' to base64..."

# Detect OS and use appropriate base64 command syntax
if [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS (BSD) version of base64
    if base64 -i "$INPUT_FILE" -o "$OUTPUT_FILE"; then
        log_message "Successfully encoded '$INPUT_FILE' to '$OUTPUT_FILE'"
        
        # Display file sizes for reference
        INPUT_SIZE=$(wc -c < "$INPUT_FILE")
        OUTPUT_SIZE=$(wc -c < "$OUTPUT_FILE")
        log_message "Input file size: $INPUT_SIZE bytes"
        log_message "Output file size: $OUTPUT_SIZE bytes"
    else
        log_error "Failed to encode file '$INPUT_FILE'"
        exit 1
    fi
else
    # Linux (GNU) version of base64
    if base64 "$INPUT_FILE" > "$OUTPUT_FILE"; then
        log_message "Successfully encoded '$INPUT_FILE' to '$OUTPUT_FILE'"
        
        # Display file sizes for reference
        INPUT_SIZE=$(wc -c < "$INPUT_FILE")
        OUTPUT_SIZE=$(wc -c < "$OUTPUT_FILE")
        log_message "Input file size: $INPUT_SIZE bytes"
        log_message "Output file size: $OUTPUT_SIZE bytes"
    else
        log_error "Failed to encode file '$INPUT_FILE'"
        exit 1
    fi
fi

log_message "Base64 encoding completed successfully!" 