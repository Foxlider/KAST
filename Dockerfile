FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Version injected by CI (e.g. 0.2.1 or 0.2.1-nightly.20260520.abc1234)
ARG APP_VERSION=0.0.0-local

# Copy solution, central package management, and project files
COPY KAST.slnx .
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY src/KAST.Core/KAST.Core.csproj src/KAST.Core/
COPY src/KAST.Infrastructure/KAST.Infrastructure.csproj src/KAST.Infrastructure/
COPY src/KAST.UI/KAST.UI.csproj src/KAST.UI/
COPY tests/KAST.Tests/KAST.Tests.csproj tests/KAST.Tests/

# Restore
RUN dotnet restore KAST.slnx

# Copy source and publish
COPY src/ src/
WORKDIR /src/src/KAST.UI
RUN dotnet publish -c Release -o /app/publish --no-restore /p:MinVerVersionOverride=$APP_VERSION && \
    test -f /app/publish/wwwroot/_framework/blazor.web.js

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Arma 3 dedicated server runtime dependencies + curl for healthcheck
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        curl \
        lib32gcc-s1 \
        lib32stdc++6 \
        libcap2 && \
    rm -rf /var/lib/apt/lists/*

# Create directories for data persistence
RUN mkdir -p /app/data /app/mods /app/servers

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
ENV ConnectionStrings__Default="Data Source=/app/data/kast.db"
ENV Kast__ModsDirectory=/app/mods
ENV Kast__ServersDirectory=/app/servers

EXPOSE 5000

ENTRYPOINT ["dotnet", "KAST.UI.dll"]
