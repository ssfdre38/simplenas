# SimpleNAS Multi-Stage Dockerfile (.NET 10)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY SimpleNAS/SimpleNAS.csproj SimpleNAS/
RUN dotnet restore SimpleNAS/SimpleNAS.csproj

# Copy source and publish
COPY SimpleNAS/ SimpleNAS/
WORKDIR /src/SimpleNAS
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime Image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Install system utilities (ZFS tools, Samba clients, procps)
RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    procps \
    samba-common-bin \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Environment
ENV ASPNETCORE_URLS=http://0.0.0.0:8000
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8000 8443

# Healthcheck
HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8000/api/auth/status || exit 1

ENTRYPOINT ["dotnet", "SimpleNAS.dll"]
