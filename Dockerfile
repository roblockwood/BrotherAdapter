# Dockerfile for BrotherAdapter MTConnect Agent
# Uses Windows container for .NET Framework 4.6.1

# Use Windows Server Core with .NET Framework
FROM mcr.microsoft.com/dotnet/framework/sdk:4.8-windowsservercore-ltsc2022 AS build

WORKDIR /src

# Copy solution and project files
COPY BrotherConnection.sln .
COPY BrotherConnection/ ./BrotherConnection/

# Restore NuGet packages
RUN nuget restore BrotherConnection.sln

# Build the application using MSBuild
RUN msbuild BrotherConnection.sln /p:Configuration=Release /p:Platform="Any CPU" /t:Build

# Runtime stage - use Windows Server Core
FROM mcr.microsoft.com/dotnet/framework/runtime:4.8-windowsservercore-ltsc2022 AS final

WORKDIR /app

# Copy published application
COPY --from=build /src/BrotherConnection/bin/Release .

# Expose MTConnect agent port
EXPOSE 7878

# Set environment variables
ENV ASPNETCORE_URLS=http://+:7878

# Health check (Windows PowerShell)
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
    CMD powershell -Command "try { $response = Invoke-WebRequest -Uri http://localhost:7878/probe -UseBasicParsing; exit $response.StatusCode -eq 200 } catch { exit 1 }"

# Run the application
ENTRYPOINT ["BrotherConnection.exe"]
