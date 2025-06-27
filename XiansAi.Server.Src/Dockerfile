# Base image for runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
USER app

# Build stage
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG configuration=Release
WORKDIR /src

# Copy only the .csproj file and restore dependencies
COPY XiansAi.Server.csproj .
RUN dotnet restore XiansAi.Server.csproj

# Copy all other source files and build the app
COPY . .
RUN dotnet build XiansAi.Server.csproj -c $configuration -o /app/build

# Publish stage
FROM build AS publish
ARG configuration=Release
RUN dotnet publish XiansAi.Server.csproj -c $configuration -o /app/publish /p:UseAppHost=false

# Final runtime image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
COPY Licenses/ ./Licenses/
ENTRYPOINT ["dotnet", "XiansAi.Server.dll"]