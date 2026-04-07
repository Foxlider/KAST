FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files
COPY KAST.slnx .
COPY src/KAST.Core/KAST.Core.csproj src/KAST.Core/
COPY src/KAST.Infrastructure/KAST.Infrastructure.csproj src/KAST.Infrastructure/
COPY src/KAST.Web/KAST.Web.csproj src/KAST.Web/

# Restore
RUN dotnet restore KAST.slnx

# Copy source and publish
COPY src/ src/
WORKDIR /src/src/KAST.Web
RUN dotnet publish -c Release -o /app/publish --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create directories for data persistence
RUN mkdir -p /app/data /app/mods /app/servers

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:5000
ENV ConnectionStrings__Default="Data Source=/app/data/kast.db"
ENV Kast__ModsDirectory=/app/mods
ENV Kast__ServersDirectory=/app/servers

EXPOSE 5000

ENTRYPOINT ["dotnet", "KAST.Web.dll"]
