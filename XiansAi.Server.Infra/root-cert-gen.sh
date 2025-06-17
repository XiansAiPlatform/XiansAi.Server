#!/bin/bash

# Root Certificate Generator
# This script generates a root certificate authority (CA) certificate
# and exports a base64 encoded PFX file containing both certificate and private key

set -e

# Default values
DEFAULT_CA_NAME="ParklyAi Root CA"
DEFAULT_DAYS=3650
OUTPUT_DIR="./.certs"
KEY_SIZE=4096

# Display usage information
usage() {
    echo "Usage: $0 [options]"
    echo "Options:"
    echo "  -p, --password PASSWORD   Password for the root certificate (required)"
    echo "  -n, --name NAME           Common name for the CA (default: $DEFAULT_CA_NAME)"
    echo "  -d, --days DAYS           Validity period in days (default: $DEFAULT_DAYS)"
    echo "  -o, --output DIR          Output directory (default: $OUTPUT_DIR)"
    echo "  -h, --help                Display this help message"
    exit 1
}

# Parse arguments
while [[ "$#" -gt 0 ]]; do
    case $1 in
        -p|--password) PASSWORD="$2"; shift ;;
        -n|--name) CA_NAME="$2"; shift ;;
        -d|--days) DAYS="$2"; shift ;;
        -o|--output) OUTPUT_DIR="$2"; shift ;;
        -h|--help) usage ;;
        *) echo "Unknown parameter: $1"; usage ;;
    esac
    shift
done

# Check if password is provided
if [ -z "$PASSWORD" ]; then
    echo "Error: Password is required"
    usage
fi

# Set defaults if not specified
CA_NAME=${CA_NAME:-$DEFAULT_CA_NAME}
DAYS=${DAYS:-$DEFAULT_DAYS}

# Create output directory if it doesn't exist
mkdir -p "$OUTPUT_DIR"

echo "Generating root certificate with the following parameters:"
echo "- CA Name: $CA_NAME"
echo "- Validity: $DAYS days"
echo "- Output Directory: $OUTPUT_DIR"

# Generate private key
echo "Generating private key..."
openssl genrsa -des3 -passout pass:"$PASSWORD" -out "$OUTPUT_DIR/$CA_NAME.key" $KEY_SIZE

# Generate root certificate with UTC time
echo "Generating root certificate..."
# Set notBefore to be 1 hour after current time to ensure it's after issuer's notBefore
openssl req -x509 -new -nodes -key "$OUTPUT_DIR/$CA_NAME.key" -sha256 -days $DAYS \
    -passin pass:"$PASSWORD" \
    -out "$OUTPUT_DIR/$CA_NAME.crt" \
    -subj "//C=US/ST=State/L=City/O=Organization/OU=IT/CN=$CA_NAME" \
    -set_serial $(date -u +%s) \

# Verify certificate was created
if [ -f "$OUTPUT_DIR/$CA_NAME.crt" ]; then
    echo "Success! Root certificate generated:"
    echo "- Private key: $OUTPUT_DIR/$CA_NAME.key"
    echo "- Certificate: $OUTPUT_DIR/$CA_NAME.crt"
    
    # Display certificate information
    echo "Certificate information:"
    openssl x509 -in "$OUTPUT_DIR/$CA_NAME.crt" -noout -text | grep "Subject:" -A 1
    openssl x509 -in "$OUTPUT_DIR/$CA_NAME.crt" -noout -text | grep "Validity" -A 2
    
    # Create PFX file (PKCS#12) containing both certificate and private key
    echo "Creating PFX file..."
    PKCS12_FILE="$OUTPUT_DIR/$CA_NAME.pfx"
    openssl pkcs12 -export -out "$PKCS12_FILE" -inkey "$OUTPUT_DIR/$CA_NAME.key" \
        -in "$OUTPUT_DIR/$CA_NAME.crt" -passin pass:"$PASSWORD" -passout pass:"$PASSWORD"
    
    # Base64 encode the PFX file
    echo "Creating base64 encoded PFX file..."
    PFX_BASE64_FILE="$OUTPUT_DIR/$CA_NAME.pfx.base64"
    cat "$PKCS12_FILE" | base64 > "$PFX_BASE64_FILE"
    
    echo "- PFX file: $PKCS12_FILE"
    echo "- Base64 encoded PFX: $PFX_BASE64_FILE"
    echo "Note: The PFX file is protected with the provided password"
else
    echo "Error: Failed to generate certificate"
    exit 1
fi