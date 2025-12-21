# Dockerfile for BrotherAdapter MTConnect Agent
# Based on the actual repository structure

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy solution and project files
COPY BrotherConnection.sln .
COPY BrotherConnection/ ./BrotherConnection/

# Restore dependencies
RUN dotnet restore BrotherConnection.sln

# Build the application
RUN dotnet build BrotherConnection.sln -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish BrotherConnection/BrotherConnection.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=publish /app/publish .

# Expose MTConnect agent port (default 7878, but may vary)
EXPOSE 7878

# Set environment variables
ENV ASPNETCORE_URLS=http://+:7878
ENV ASPNETCORE_ENVIRONMENT=Production

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:7878/probe || exit 1

# Run the application
ENTRYPOINT ["dotnet", "BrotherConnection.dll"]

