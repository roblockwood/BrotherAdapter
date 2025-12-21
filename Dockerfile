# Dockerfile for BrotherAdapter MTConnect Agent (Linux/Mono version)
# Uses Mono to run .NET Framework 4.6.1 on Linux

FROM mono:latest AS build

WORKDIR /src

# Install build tools
RUN apt-get update && apt-get install -y \
    nuget \
    msbuild \
    && rm -rf /var/lib/apt/lists/*

# Copy solution and project files
COPY BrotherConnection.sln .
COPY BrotherConnection/ ./BrotherConnection/

# Restore NuGet packages
RUN nuget restore BrotherConnection.sln -NonInteractive

# Build the application using MSBuild
RUN msbuild BrotherConnection.sln /p:Configuration=Release /p:Platform="Any CPU" /t:Build

# Runtime stage - use same base image but clean up build tools
FROM mono:latest

WORKDIR /app

# Install curl for health checks (and remove build tools to reduce size)
RUN apt-get update && apt-get install -y \
    curl \
    && apt-get remove -y --purge nuget msbuild \
    && apt-get autoremove -y \
    && rm -rf /var/lib/apt/lists/*

# Copy published application
COPY --from=build /src/BrotherConnection/bin/Release .

# Expose MTConnect agent port
EXPOSE 7878

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:7878/probe || exit 1

# Run the application
ENTRYPOINT ["mono", "BrotherConnection.exe"]
