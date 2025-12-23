# Dockerfile for BrotherAdapter MTConnect Agent (Linux/Mono version)
# Uses Mono to run .NET Framework 4.6.1 on Linux

FROM mono:latest AS build

WORKDIR /src

# Fix Debian Buster EOL - use archive repositories
RUN sed -i 's|http://deb.debian.org/debian|http://archive.debian.org/debian|g' /etc/apt/sources.list && \
    sed -i 's|http://security.debian.org/debian-security|http://archive.debian.org/debian-security|g' /etc/apt/sources.list && \
    sed -i '/stretch-updates/d' /etc/apt/sources.list || true

# Install build tools
# mono-complete includes MSBuild, and we'll install NuGet separately
RUN apt-get update && apt-get install -y \
    mono-complete \
    ca-certificates-mono \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Download and install NuGet
RUN curl -L https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -o /usr/local/bin/nuget.exe && \
    chmod +x /usr/local/bin/nuget.exe

# Setup MSBuild wrapper script (MSBuild.dll location may vary by Mono version)
RUN MSBUILD_DLL=$(find /usr/lib/mono -name "MSBuild.dll" 2>/dev/null | head -1); \
    if [ -z "$MSBUILD_DLL" ]; then MSBUILD_DLL="/usr/lib/mono/msbuild/15.0/bin/MSBuild.dll"; fi; \
    printf '#!/bin/bash\nmono "%s" "$@"\n' "$MSBUILD_DLL" > /usr/local/bin/msbuild && \
    chmod +x /usr/local/bin/msbuild

# Copy solution and project files
COPY BrotherConnection.sln .
COPY BrotherConnection/ ./BrotherConnection/

# Restore NuGet packages
RUN mono /usr/local/bin/nuget.exe restore BrotherConnection.sln -NonInteractive

# Build the application using MSBuild
RUN msbuild BrotherConnection.sln /p:Configuration=Release /p:Platform="Any CPU" /t:Build

# Runtime stage - use same base image but clean up build tools
FROM mono:latest

WORKDIR /app

# Fix Debian Buster EOL - use archive repositories
RUN sed -i 's|http://deb.debian.org/debian|http://archive.debian.org/debian|g' /etc/apt/sources.list && \
    sed -i 's|http://security.debian.org/debian-security|http://archive.debian.org/debian-security|g' /etc/apt/sources.list && \
    sed -i '/stretch-updates/d' /etc/apt/sources.list || true

# Install curl for health checks
RUN apt-get update && apt-get install -y \
    curl \
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
