FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /app

# Copy csproj and restore dependencies
COPY *.csproj ./
RUN dotnet restore

# Copy the rest of the files and build
COPY . ./
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:7.0
WORKDIR /app
COPY --from=build /app/out .

# Environment variable to specify which service to run
ENV SERVICE_TYPE="--all"

# Expose ports
EXPOSE 80
EXPOSE 443

# Make entrypoint.sh script
RUN echo '#!/bin/bash\ndotnet XiansAi.Server.dll $SERVICE_TYPE' > /app/entrypoint.sh \
    && chmod +x /app/entrypoint.sh

# Set the entry point
ENTRYPOINT ["/app/entrypoint.sh"] 